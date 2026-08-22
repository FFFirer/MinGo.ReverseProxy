using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MinGo.Core.Services;
using MinGo.DataPlane.ConfigSync;
using MinGo.DataPlane.Grpc;
using MinGo.DataPlane.Heartbeat;
using MinGo.DataPlane.Kestrel;
using MinGo.DataPlane.Telemetry;
using Moq;

namespace MinGo.DataPlane.Tests;

/// <summary>
/// E2E 测试：验证 YARP 反向代理在各种场景下的正确性
/// 使用 HostBuilder + LoadFromConfig + InMemoryCollection 动态配置
/// </summary>
public class ReverseProxyE2ETests : IAsyncLifetime
{
    private IHost _dataPlane = null!;
    private IHost _mockBackend = null!;
    private string _dataPlaneUrl = null!;
    private string _backendUrl = null!;
    private Func<HttpContext, Task> _backendHandler = null!;
    private readonly List<(string Path, string Query)> _capturedRequests = new();
    private InMemoryConfigurationSource _configSource = null!;

    public async Task InitializeAsync()
    {
        _backendHandler = DefaultHandler;

        // 1. 启动 mock 后端
        _mockBackend = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseKestrel();
                web.UseUrls("http://127.0.0.1:0");
                web.Configure(app => app.Run(async ctx =>
                {
                    _capturedRequests.Add((ctx.Request.Path.Value ?? "", ctx.Request.QueryString.Value ?? ""));
                    await _backendHandler(ctx);
                }));
            })
            .Build();
        await _mockBackend.StartAsync();
        _backendUrl = GetUrl(_mockBackend);

        // 2. 启动 DataPlane
        _configSource = new InMemoryConfigurationSource();
        _configSource.Set("ReverseProxy:Clusters:backend:Destinations:d1:Address", _backendUrl);
        _configSource.Set("ReverseProxy:Routes:r1:ClusterId", "backend");
        _configSource.Set("ReverseProxy:Routes:r1:Match:Path", "/{**catchall}");

        _dataPlane = new HostBuilder()
            .ConfigureAppConfiguration((_, config) => { config.Add(_configSource); })
            .ConfigureWebHost(web =>
            {
                web.UseKestrel();
                web.UseUrls("http://127.0.0.1:0");
                web.ConfigureServices((ctx, services) =>
                {
                    services.AddSingleton<TelemetryStore>();
                    services.AddReverseProxy()
                        .LoadFromConfig(ctx.Configuration.GetSection("ReverseProxy"));
                    services.AddHealthChecks();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseGatewayTelemetry();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapHealthChecks("/healthz/live",
                            new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
                            { Predicate = _ => false });
                        endpoints.MapHealthChecks("/healthz/ready",
                            new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
                            {
                                Predicate = _ => false,
                                ResponseWriter = async (ctx, _) =>
                                {
                                    ctx.Response.ContentType = "application/json";
                                    ctx.Response.StatusCode = 200;
                                    await ctx.Response.WriteAsync("""{"status":"Healthy"}""");
                                }
                            });
                        endpoints.MapReverseProxy();
                    });
                });
            })
            .Build();
        await _dataPlane.StartAsync();
        _dataPlaneUrl = GetUrl(_dataPlane);
    }

    public async Task DisposeAsync()
    {
        await _dataPlane.StopAsync();
        _dataPlane.Dispose();
        await _mockBackend.StopAsync();
        _mockBackend.Dispose();
    }

    private static string GetUrl(IHost host)
    {
        var server = host.Services.GetRequiredService<IServer>();
        return server.Features.Get<IServerAddressesFeature>()!.Addresses.First();
    }

    private static Task DefaultHandler(HttpContext ctx)
    {
        ctx.Response.StatusCode = 200;
        return ctx.Response.WriteAsync("OK from backend");
    }

    private void ResetBackend(Func<HttpContext, Task>? handler = null)
    {
        _capturedRequests.Clear();
        _backendHandler = handler ?? DefaultHandler;
    }

    #region Helper

    /// <summary>
    /// 更新 YARP 路由配置（通过 InMemoryConfigurationSource 动态修改）
    /// </summary>
    private void UpdateRoute(string routeId, string clusterId, string path = "/{**catchall}", string? host = null)
    {
        _configSource.Set($"ReverseProxy:Routes:{routeId}:ClusterId", clusterId);
        _configSource.Set($"ReverseProxy:Routes:{routeId}:Match:Path", path);
        if (host != null)
            _configSource.Set($"ReverseProxy:Routes:{routeId}:Match:Hosts:0", host);
    }

    private void AddCluster(string clusterId, string? destAddress = null)
    {
        _configSource.Set($"ReverseProxy:Clusters:{clusterId}:Destinations:d1:Address", destAddress ?? _backendUrl);
    }

    #endregion

    // ========== 基本代理 ==========

    [Fact]
    public async Task Proxy_BasicRequest_ReturnsBackendResponse()
    {
        ResetBackend();
        using var client = new HttpClient { BaseAddress = new Uri(_dataPlaneUrl) };
        var resp = await client.GetAsync("/test");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("OK from backend", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Proxy_PreservesRequestMethod()
    {
        ResetBackend(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync($"method={ctx.Request.Method}");
        });

        using var client = new HttpClient { BaseAddress = new Uri(_dataPlaneUrl) };
        Assert.Contains("method=GET", await (await client.GetAsync("/x")).Content.ReadAsStringAsync());
        Assert.Contains("method=POST", await (await client.PostAsync("/x", new StringContent("b"))).Content.ReadAsStringAsync());
        Assert.Contains("method=PUT", await (await client.PutAsync("/x", new StringContent("b"))).Content.ReadAsStringAsync());
        Assert.Contains("method=DELETE", await (await client.DeleteAsync("/x")).Content.ReadAsStringAsync());
    }

    // ========== 路径转发 ==========

    [Fact]
    public async Task Proxy_PathForwarded()
    {
        ResetBackend(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync($"path={ctx.Request.Path}");
        });

        using var client = new HttpClient { BaseAddress = new Uri(_dataPlaneUrl) };
        var resp = await client.GetAsync("/api/users/123");
        Assert.Equal("path=/api/users/123", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Proxy_NestedPathForwarded()
    {
        ResetBackend(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync($"path={ctx.Request.Path}");
        });

        using var client = new HttpClient { BaseAddress = new Uri(_dataPlaneUrl) };
        var resp = await client.GetAsync("/a/b/c/d/e");
        Assert.Equal("path=/a/b/c/d/e", await resp.Content.ReadAsStringAsync());
    }

    // ========== 查询字符串 ==========

    [Fact]
    public async Task Proxy_QueryStringPreserved()
    {
        ResetBackend(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync($"query={ctx.Request.QueryString}");
        });

        using var client = new HttpClient { BaseAddress = new Uri(_dataPlaneUrl) };
        var resp = await client.GetAsync("/search?q=test&page=2");
        Assert.Equal("query=?q=test&page=2", await resp.Content.ReadAsStringAsync());
    }

    // ========== 请求头转发 ==========

    [Fact]
    public async Task Proxy_RequestHeadersForwarded()
    {
        ResetBackend(async ctx =>
        {
            var custom = ctx.Request.Headers["X-Custom"].FirstOrDefault() ?? "missing";
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync($"custom={custom}");
        });

        using var client = new HttpClient { BaseAddress = new Uri(_dataPlaneUrl) };
        client.DefaultRequestHeaders.Add("X-Custom", "hello");
        var resp = await client.GetAsync("/");
        Assert.Equal("custom=hello", await resp.Content.ReadAsStringAsync());
    }

    // ========== 响应状态码透传 ==========

    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(204)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public async Task Proxy_StatusCodePassthrough(int statusCode)
    {
        ResetBackend(ctx => { ctx.Response.StatusCode = statusCode; return Task.CompletedTask; });

        using var client = new HttpClient { BaseAddress = new Uri(_dataPlaneUrl) };
        var resp = await client.GetAsync("/");
        Assert.Equal(statusCode, (int)resp.StatusCode);
    }

    // ========== 响应头透传 ==========

    [Fact]
    public async Task Proxy_ResponseHeadersPassthrough()
    {
        ResetBackend(ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.Headers["X-Backend-Header"] = "backend-value";
            ctx.Response.Headers["X-Request-Id"] = "req-12345";
            return Task.CompletedTask;
        });

        using var client = new HttpClient { BaseAddress = new Uri(_dataPlaneUrl) };
        var resp = await client.GetAsync("/");
        Assert.Equal("backend-value", resp.Headers.GetValues("X-Backend-Header").First());
        Assert.Equal("req-12345", resp.Headers.GetValues("X-Request-Id").First());
    }

    // ========== 响应体 ==========

    [Fact]
    public async Task Proxy_LargeResponseBody()
    {
        var largeBody = new string('X', 100_000);
        ResetBackend(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync(largeBody);
        });

        using var client = new HttpClient { BaseAddress = new Uri(_dataPlaneUrl) };
        var resp = await client.GetAsync("/");
        Assert.Equal(largeBody, await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Proxy_JsonResponseBody()
    {
        ResetBackend(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("""{"name":"test","value":42}""");
        });

        using var client = new HttpClient { BaseAddress = new Uri(_dataPlaneUrl) };
        var resp = await client.GetAsync("/api/data");
        Assert.Equal("application/json", resp.Content.Headers.ContentType?.MediaType);
        Assert.Equal("""{"name":"test","value":42}""", await resp.Content.ReadAsStringAsync());
    }

    // ========== POST 请求体 ==========

    [Fact]
    public async Task Proxy_PostBodyForwarded()
    {
        ResetBackend(async ctx =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync($"received={body}");
        });

        using var client = new HttpClient { BaseAddress = new Uri(_dataPlaneUrl) };
        var resp = await client.PostAsync("/ingest", new StringContent("test-payload", Encoding.UTF8, "text/plain"));
        Assert.Contains("test-payload", await resp.Content.ReadAsStringAsync());
    }

    // ========== 健康检查 ==========

    [Fact]
    public async Task HealthCheck_Live_Returns200()
    {
        using var client = new HttpClient { BaseAddress = new Uri(_dataPlaneUrl) };
        var resp = await client.GetAsync("/healthz/live");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task HealthCheck_Ready_Returns200()
    {
        using var client = new HttpClient { BaseAddress = new Uri(_dataPlaneUrl) };
        var resp = await client.GetAsync("/healthz/ready");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ========== 后端请求捕获 ==========

    [Fact]
    public async Task Proxy_RequestCapturedByBackend()
    {
        ResetBackend(ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; });

        using var client = new HttpClient { BaseAddress = new Uri(_dataPlaneUrl) };
        await client.GetAsync("/tracked?x=1");

        Assert.Single(_capturedRequests);
        Assert.Equal("/tracked", _capturedRequests[0].Path);
        Assert.Equal("?x=1", _capturedRequests[0].Query);
    }

    // ========== 遥测隔离 ==========

    [Fact]
    public async Task Telemetry_HealthCheck_NotRecorded()
    {
        var store = _dataPlane.Services.GetRequiredService<TelemetryStore>();
        using var client = new HttpClient { BaseAddress = new Uri(_dataPlaneUrl) };
        await client.GetAsync("/healthz/live");
        await client.GetAsync("/healthz/ready");
        Assert.False(store.Metrics.ContainsKey("gateway.requests.total"));
    }

    [Fact]
    public async Task Telemetry_ProxyRequest_Recorded()
    {
        ResetBackend();
        var store = _dataPlane.Services.GetRequiredService<TelemetryStore>();
        using var client = new HttpClient { BaseAddress = new Uri(_dataPlaneUrl) };
        await client.GetAsync("/api/test");
        Assert.True(store.Metrics.ContainsKey("gateway.requests.total"));
    }

    // ========== 二进制响应 ==========

    [Fact]
    public async Task Proxy_BinaryResponse()
    {
        var data = new byte[256];
        Random.Shared.NextBytes(data);

        ResetBackend(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/octet-stream";
            await ctx.Response.Body.WriteAsync(data);
        });

        using var client = new HttpClient { BaseAddress = new Uri(_dataPlaneUrl) };
        var resp = await client.GetAsync("/download");
        Assert.Equal(data, await resp.Content.ReadAsByteArrayAsync());
    }

    // ========== 配置热更新 ==========

    [Fact]
    public async Task Proxy_ConfigHotReload_NewRouteWorks()
    {
        ResetBackend();
        // 初始路由已经配置 (/{**catchall})

        // 添加新路由
        UpdateRoute("r2", "backend", path: "/new/{**catchall}");
        AddCluster("backend");
        await Task.Delay(300); // 等待 YARP 处理配置变更

        using var client = new HttpClient { BaseAddress = new Uri(_dataPlaneUrl) };
        var resp = await client.GetAsync("/new/test");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // 清理
        _configSource.Set("ReverseProxy:Routes:r2:ClusterId", null!);
    }

    // ========== InMemoryConfigurationSource ==========

    private sealed class InMemoryConfigurationSource : IConfigurationSource
    {
        private readonly Dictionary<string, string?> _data = new(StringComparer.OrdinalIgnoreCase);

        public void Set(string key, string? value)
        {
            _data[key] = value;
        }

        public IConfigurationProvider Build(IConfigurationBuilder builder) => new InMemoryProvider(this);

        private sealed class InMemoryProvider : ConfigurationProvider
        {
            private readonly InMemoryConfigurationSource _source;

            public InMemoryProvider(InMemoryConfigurationSource source)
            {
                _source = source;
            }

            public override void Load()
            {
                Data = new Dictionary<string, string?>(_source._data.Where(kv => kv.Value != null),
                    StringComparer.OrdinalIgnoreCase);
            }

            public override string? ToString() => "InMemoryTestConfig";
        }
    }
}
