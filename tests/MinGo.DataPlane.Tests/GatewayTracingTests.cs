using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MinGo.Core.Services;
using MinGo.DataPlane.Telemetry;

namespace MinGo.DataPlane.Tests;

/// <summary>
/// E2E 验证：真实 OpenTelemetry 装配下的请求链路采集
/// 契约见 openspec/changes/unify-otel-request-collection/specs/request-tracing
/// 这里刻意走 Program.cs 使用的同一个 <see cref="GatewayOpenTelemetry"/> 装配入口，
/// 因为本阶段要修的正是"订阅的 source 名写错不会报错、只会永远没有数据"这类静默失效
///
/// 同进程内并行的其它测试宿主也会经由静态 ActivitySource 产生 span，
/// 并被本测试的 processor 记进同一个 store。因此断言一律以"本次请求独有的路径"为锚点，
/// 再顺着访问日志的 TraceId 找回对应 span —— 这条跳转链本身就是要交付的能力
/// </summary>
public class GatewayTracingTests : IAsyncLifetime
{
    private IHost _backend = null!;
    private IHost _gateway = null!;
    private HttpClient _client = null!;
    private TelemetryStore _store = null!;
    private RequestDelegate _backendHandler = OkBackend;

    private static Task OkBackend(HttpContext ctx)
    {
        ctx.Response.StatusCode = 200;
        return ctx.Response.WriteAsync("OK from backend");
    }

    public async Task InitializeAsync()
    {
        _backend = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseKestrel();
                web.UseUrls("http://127.0.0.1:0");
                web.Configure(app => app.Run(async ctx => await _backendHandler(ctx)));
            })
            .Build();
        await _backend.StartAsync();

        var configSource = new InMemoryConfigurationSource();
        configSource.Set("ReverseProxy:Clusters:backend:Destinations:d1:Address", GetUrl(_backend));
        configSource.Set("ReverseProxy:Routes:r1:ClusterId", "backend");
        configSource.Set("ReverseProxy:Routes:r1:Match:Path", "/{**catchall}");

        _gateway = new HostBuilder()
            .ConfigureAppConfiguration((_, config) => config.Add(configSource))
            .ConfigureWebHost(web =>
            {
                web.UseKestrel();
                web.UseUrls("http://127.0.0.1:0");
                web.ConfigureServices((ctx, services) =>
                {
                    services.AddSingleton<TelemetryStore>();
                    services.AddReverseProxy()
                        .LoadFromConfig(ctx.Configuration.GetSection("ReverseProxy"));

                    // OTLP 指向未监听端口：本组测试只验采集与本地落地。
                    // 导出失败由 SDK 内部记录，不影响断言，也不在请求路径上等待
                    services.AddOpenTelemetry().AddGatewayTelemetry(
                        "min-go-dataplane-test",
                        new Uri("http://127.0.0.1:43199"));
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseGatewayTelemetry();
                    app.UseEndpoints(endpoints => endpoints.MapReverseProxy());
                });
            })
            .Build();
        await _gateway.StartAsync();

        _store = _gateway.Services.GetRequiredService<TelemetryStore>();
        _client = new HttpClient { BaseAddress = new Uri(GetUrl(_gateway)) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _gateway.StopAsync();
        _gateway.Dispose();
        await _backend.StopAsync();
        _backend.Dispose();
    }

    private static string GetUrl(IHost host)
    {
        var server = host.Services.GetRequiredService<IServer>();
        return server.Features.Get<IServerAddressesFeature>()!.Addresses.First();
    }

    /// <summary>
    /// 读取 span 标签的原始值。
    /// Activity.Tags 只给出字符串形态，语义约定里的 http.response.status_code 是整数，
    /// 因此断言走 EnumerateTagObjects —— 这也是 OTel 导出侧读取标签的方式
    /// </summary>
    private static string? Tag(Activity activity, string key)
    {
        foreach (var tag in activity.EnumerateTagObjects())
        {
            if (tag.Key == key)
            {
                return tag.Value?.ToString();
            }
        }

        return null;
    }

    /// <summary>
    /// 代理一个带唯一路径的请求，并返回它的访问日志与两侧 span
    /// </summary>
    private async Task<(AccessLogEntry Log, Activity Gateway, Activity Forwarder)> ProxyAsync(int expectedStatus = 200)
    {
        var path = $"/tracing/{Guid.NewGuid():N}";
        var response = await _client.GetAsync(path);
        Assert.Equal((HttpStatusCode)expectedStatus, response.StatusCode);

        var log = _store.AccessLogs.Single(l => l.Path == path);
        Assert.NotNull(log.TraceId);

        var gateway = _store.Traces.Single(a =>
            a.Source.Name == GatewayOpenTelemetry.GatewaySourceName && a.TraceId.ToString() == log.TraceId);
        var forwarder = _store.Traces.Single(a =>
            a.Source.Name == GatewayOpenTelemetry.YarpSourceName && a.TraceId.ToString() == log.TraceId);

        return (log, gateway, forwarder);
    }

    [Fact]
    public async Task ProxiedRequest_LandsLocalTraces_WithoutWaitingForBatchFlush()
    {
        // SimpleActivityExportProcessor：Activity 停止即入队，看板不必等导出周期
        var (_, gateway, forwarder) = await ProxyAsync();

        Assert.NotNull(gateway);
        Assert.NotNull(forwarder);
    }

    [Fact]
    public async Task ProxiedRequest_RecordsGatewayAndForwarderSpans()
    {
        var (_, gateway, forwarder) = await ProxyAsync();

        // 修复前 AddSource 只订阅 Gateway，proxy.forwarder 从未进入链路
        Assert.Equal("proxy.forwarder", forwarder.OperationName);
        Assert.Equal(gateway.TraceId, forwarder.TraceId);
    }

    [Fact]
    public async Task GatewaySpan_CarriesBoundedDimensionsAndSemanticAttributes()
    {
        var (log, gateway, _) = await ProxyAsync();

        Assert.Equal("r1", Tag(gateway, "route"));
        Assert.Equal("backend", Tag(gateway, "cluster"));
        Assert.Equal("d1", Tag(gateway, "destination"));
        Assert.Equal("200", Tag(gateway, "http.response.status_code"));
        Assert.Equal("GET", Tag(gateway, "http.request.method"));
        // 基数有界的 route 维度归一为 r1，访问日志仍保留原始路径供排查
        Assert.Equal(log.Path, Tag(gateway, "url.path"));
    }

    [Fact]
    public async Task ForwarderSpan_AlreadyCarriesYarpDestinationDimension()
    {
        // 目的地维度在链路侧是现成的，无需自造：规范据此约定网关不重复造键
        var (_, _, forwarder) = await ProxyAsync();

        Assert.Equal("r1", Tag(forwarder, "proxy.route_id"));
        Assert.Equal("backend", Tag(forwarder, "proxy.cluster_id"));
        Assert.Equal("d1", Tag(forwarder, "proxy.destination_id"));
    }

    [Fact]
    public async Task AccessLog_CarriesW3cTraceAndSpanId_WhenListenerPresent()
    {
        var (log, gateway, _) = await ProxyAsync();

        // 有 Activity 时关联键必须是 W3C TraceId，而不是 ASP.NET Core 的请求 id
        Assert.Equal(gateway.TraceId.ToHexString(), log.TraceId);
        Assert.Equal(gateway.SpanId.ToHexString(), log.SpanId);
    }

    [Fact]
    public async Task UpstreamServerError_MarksGatewaySpanError()
    {
        // 与 errors 指标同一判定口径：上游 5xx 也是网关侧失败，
        // 只按"抛异常"标错会让链路后端把 500/502 显示成成功
        _backendHandler = ctx =>
        {
            ctx.Response.StatusCode = 500;
            return Task.CompletedTask;
        };

        try
        {
            var (log, gateway, _) = await ProxyAsync(500);

            Assert.Equal(500, log.StatusCode);
            Assert.Equal("500", Tag(gateway, "http.response.status_code"));
            Assert.Equal(ActivityStatusCode.Error, gateway.Status);
        }
        finally
        {
            _backendHandler = OkBackend;
        }
    }

    [Fact]
    public async Task SuccessfulRequest_SpanIsNotMarkedError()
    {
        var (_, gateway, _) = await ProxyAsync();

        Assert.NotEqual(ActivityStatusCode.Error, gateway.Status);
    }
}
