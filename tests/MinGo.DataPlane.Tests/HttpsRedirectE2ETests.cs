using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MinGo.DataPlane.ConfigSync;
using MinGo.DataPlane.Kestrel;
using MinGo.DataPlane.Middleware;

namespace MinGo.DataPlane.Tests;

/// <summary>
/// 跳转链路端到端：真实 Kestrel（HTTP + HTTPS）+ 真实证书选择器 + YARP 路由。
/// </summary>
public class HttpsRedirectE2ETests : IAsyncLifetime
{
    private readonly HttpsRedirectOptions _options = new()
    {
        Enabled = true,
        HttpOnlyHosts = ["plain.example.com"]
    };

    private IHost _dataPlane = null!;
    private IHost _backend = null!;
    private int _httpPort;
    private int _httpsPort;

    public async Task InitializeAsync()
    {
        _backend = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseKestrel();
                web.UseUrls("http://127.0.0.1:0");
                web.Configure(app => app.Run(async ctx =>
                {
                    using var reader = new StreamReader(ctx.Request.Body);
                    var body = await reader.ReadToEndAsync();
                    ctx.Response.StatusCode = 200;
                    await ctx.Response.WriteAsync(
                        $"{ctx.Request.Method}|{body}|{ctx.Request.Path}{ctx.Request.QueryString}");
                }));
            })
            .Build();
        await _backend.StartAsync();
        var backendUrl = GetUrls(_backend).First();

        var certificates = new[]
        {
            TestCertificateFactory.Create("api.example.com"),
            TestCertificateFactory.Create("plain.example.com"),
            TestCertificateFactory.Create("expired.example.net", expired: true),
            TestCertificateFactory.Create("*.wild.example.com")
        };
        var provider = new DataPlaneConfigProvider(
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<DataPlaneConfigProvider>.Instance);
        provider.UpdateCertificates(certificates);

        _httpPort = GetFreePort();
        _httpsPort = GetFreePort();
        _options.Port = _httpsPort;

        DataPlaneCertificateSelector selector = null!;

        _dataPlane = new HostBuilder()
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReverseProxy:Routes:api:ClusterId"] = "backend",
                ["ReverseProxy:Routes:api:Match:Hosts:0"] = "api.example.com",
                ["ReverseProxy:Routes:api:Match:Path"] = "/{**catchall}",
                ["ReverseProxy:Routes:plain:ClusterId"] = "backend",
                ["ReverseProxy:Routes:plain:Match:Hosts:0"] = "plain.example.com",
                ["ReverseProxy:Routes:plain:Match:Path"] = "/{**catchall}",
                ["ReverseProxy:Routes:expired:ClusterId"] = "backend",
                ["ReverseProxy:Routes:expired:Match:Hosts:0"] = "expired.example.net",
                ["ReverseProxy:Routes:expired:Match:Path"] = "/{**catchall}",
                ["ReverseProxy:Routes:wild:ClusterId"] = "backend",
                ["ReverseProxy:Routes:wild:Match:Hosts:0"] = "*.wild.example.com",
                ["ReverseProxy:Routes:wild:Match:Path"] = "/{**catchall}",
                ["ReverseProxy:Clusters:backend:Destinations:d1:Address"] = backendUrl
            }))
            .ConfigureWebHost(web =>
            {
                web.UseKestrel();
                web.UseUrls($"http://127.0.0.1:{_httpPort}", $"https://127.0.0.1:{_httpsPort}");
                web.ConfigureServices((ctx, services) =>
                {
                    services.AddSingleton(provider);
                    services.AddCertificateServices();
                    services.Configure<ForwardedHeadersOptions>(o =>
                    {
                        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                                           | ForwardedHeaders.XForwardedProto
                                           | ForwardedHeaders.XForwardedHost;
                        o.KnownProxies.Clear();
                        o.KnownIPNetworks.Clear();
                    });
                    services.AddHttpsRedirection(o =>
                    {
                        o.HttpsPort = _httpsPort;
                        o.RedirectStatusCode = StatusCodes.Status308PermanentRedirect;
                    });
                    services.AddReverseProxy()
                        .LoadFromConfig(ctx.Configuration.GetSection("ReverseProxy"));
                    services.AddHealthChecks();
                });
                web.Configure(app =>
                {
                    app.UseForwardedHeaders();
                    app.MapWhen(
                        ctx => HttpsRedirectPolicy.NeedsRedirect(ctx, _options, selector),
                        branch => branch.UseHttpsRedirection());
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapHealthChecks("/healthz/live",
                            new HealthCheckOptions { Predicate = _ => false });
                        endpoints.MapReverseProxy();
                    });
                });
            })
            .Build();

        selector = _dataPlane.Services.GetRequiredService<DataPlaneCertificateSelector>();
        selector.ReloadFromProvider();
        await _dataPlane.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _dataPlane.StopAsync();
        _dataPlane.Dispose();
        await _backend.StopAsync();
        _backend.Dispose();
    }

    private static IEnumerable<string> GetUrls(IHost host) => host.Services.GetRequiredService<IServer>()
        .Features.Get<IServerAddressesFeature>()!.Addresses;

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private HttpClient CreatePlainClient() => new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        BaseAddress = new Uri($"http://127.0.0.1:{_httpPort}")
    };

    /// <summary>跟随跳转，并把 api.example.com 解析回本机对应端口。</summary>
    private HttpClient CreateFollowClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            UseProxy = false,
            SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true },
            ConnectCallback = async (ctx, cancellationToken) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                await socket.ConnectAsync(IPAddress.Loopback, ctx.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
        };
        return new HttpClient(handler)
        {
            BaseAddress = new Uri($"http://127.0.0.1:{_httpPort}")
        };
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string host, string path, string? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Host = host;
        if (body != null)
            request.Content = new StringContent(body);
        return request;
    }

    [Fact]
    public async Task PlainRequest_ToCertHost_Returns308_WithPortAndQueryPreserved()
    {
        using var client = CreatePlainClient();
        using var request = CreateRequest(HttpMethod.Get, "api.example.com:8080", "/ping?a=1&b=2");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PermanentRedirect, response.StatusCode);
        Assert.Equal($"https://api.example.com:{_httpsPort}/ping?a=1&b=2", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Post_FollowsRedirect_PreservingMethodAndBody()
    {
        using var client = CreateFollowClient();
        using var request = CreateRequest(HttpMethod.Post, "api.example.com:8080", "/echo?x=1", "payload");

        var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("POST|payload|/echo", text);
    }

    [Fact]
    public async Task WildcardCertificate_RedirectsSubdomain()
    {
        using var client = CreatePlainClient();
        using var request = CreateRequest(HttpMethod.Get, "shop.wild.example.com:8080", "/ping");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PermanentRedirect, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredCertificate_StillRedirects()
    {
        using var client = CreatePlainClient();
        using var request = CreateRequest(HttpMethod.Get, "expired.example.net:8080", "/ping");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PermanentRedirect, response.StatusCode);
        // 过期证书仍在交付快照里，因此照常准入并跳向 HTTPS；后续握手由客户端证书校验拒绝
        Assert.Equal($"https://expired.example.net:{_httpsPort}/ping", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task HttpOnlyHost_IsProxiedNotRedirected()
    {
        using var client = CreatePlainClient();
        using var request = CreateRequest(HttpMethod.Get, "plain.example.com:8080", "/ping");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("GET||/ping", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task HostWithoutCertificate_IsNotRedirected()
    {
        using var client = CreatePlainClient();
        using var request = CreateRequest(HttpMethod.Get, "unknown.example.com:8080", "/ping");

        var response = await client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.PermanentRedirect, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task HealthEndpoint_IsNotRedirected()
    {
        using var client = CreatePlainClient();
        using var request = CreateRequest(HttpMethod.Get, "api.example.com:8080", "/healthz/live");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    // ========== Cloudflare Tunnel 场景（前置 TLS 终结） ==========

    /// <summary>
    /// 模拟 Cloudflare Tunnel：客户端 HTTPS → CF → HTTP + X-Forwarded-Proto: https → DataPlane:8080。
    /// ForwardedHeadersMiddleware 解析 X-Forwarded-Proto 后覆盖 Request.Scheme，
    /// NeedsRedirect 的 IsHttps 检查返回 true，不触发跳转。
    /// </summary>
    [Fact]
    public async Task CloudflareTunnel_XForwardedProtoHttps_IsNotRedirected()
    {
        using var client = CreatePlainClient();
        using var request = CreateRequest(HttpMethod.Get, "api.example.com", "/ping");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "203.0.113.50");

        var response = await client.SendAsync(request);

        // 不应跳转，应正常代理到后端
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Contains("GET||/ping", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// 前置代理场景：X-Forwarded-Proto 为 http（非 HTTPS），仍应触发跳转。
    /// </summary>
    [Fact]
    public async Task ForwardedProtoHttp_StillRedirects()
    {
        using var client = CreatePlainClient();
        using var request = CreateRequest(HttpMethod.Get, "api.example.com:8080", "/ping");
        request.Headers.Add("X-Forwarded-Proto", "http");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PermanentRedirect, response.StatusCode);
        Assert.Equal($"https://api.example.com:{_httpsPort}/ping", response.Headers.Location!.ToString());
    }

    /// <summary>
    /// 前置代理场景：X-Forwarded-Host 覆盖原始 Host，跳转 Location 应使用转发后的 Host。
    /// </summary>
    [Fact]
    public async Task ForwardedHost_UsedInRedirectLocation()
    {
        using var client = CreatePlainClient();
        using var request = CreateRequest(HttpMethod.Get, "internal-host:8080", "/ping");
        request.Headers.Add("X-Forwarded-Proto", "http");
        request.Headers.Add("X-Forwarded-Host", "api.example.com");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PermanentRedirect, response.StatusCode);
        // Location 应使用 X-Forwarded-Host 的值
        Assert.Equal($"https://api.example.com:{_httpsPort}/ping", response.Headers.Location!.ToString());
    }
}
