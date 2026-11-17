using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Yarp.ReverseProxy.Configuration;

namespace MinGo.DataPlane.Tests;

/// <summary>
/// 匹配优先级实测（spike）：控制面的"域名 + 路径去重"口径必须和 YARP 真实的路由匹配语义一致，
/// 否则会拦掉本该共存的规则或放过本该视为重复的规则。这里锁三条框架行为：
/// <list type="bullet">
/// <item>精确域名优先于 <c>*.</c> 通配域名，两者可安全共存；</item>
/// <item>域名匹配大小写不敏感；</item>
/// <item>路径字面量匹配大小写不敏感。</item>
/// </list>
/// </summary>
public class YarpMatchPrecedenceTests : IAsyncLifetime
{
    private IHost _proxy = null!;
    private string _proxyUrl = null!;

    public async Task InitializeAsync()
    {
        var routes = new[]
        {
            new RouteConfig
            {
                RouteId = "wild",
                ClusterId = "wild-cluster",
                Match = new RouteMatch { Hosts = new[] { "*.example.com" }, Path = "/{**catch-all}" },
            },
            new RouteConfig
            {
                RouteId = "exact",
                ClusterId = "exact-cluster",
                Match = new RouteMatch { Hosts = new[] { "API.Example.COM" }, Path = "/Api/{**catch-all}" },
            },
        };

        var clusters = new[]
        {
            new ClusterConfig { ClusterId = "wild-cluster", Destinations = new Dictionary<string, DestinationConfig> { ["d"] = new() { Address = await StartBackend("wild") } } },
            new ClusterConfig { ClusterId = "exact-cluster", Destinations = new Dictionary<string, DestinationConfig> { ["d"] = new() { Address = await StartBackend("exact") } } },
        };

        _proxy = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseKestrel();
                web.UseUrls("http://127.0.0.1:0");
                web.ConfigureServices((_, services) =>
                {
                    services.AddRouting();
                    services.AddReverseProxy().LoadFromMemory(routes, clusters);
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapReverseProxy());
                });
            })
            .Build();

        await _proxy.StartAsync();
        _proxyUrl = GetUrl(_proxy);
    }

    public async Task DisposeAsync()
    {
        await _proxy.StopAsync();
        _proxy.Dispose();
    }

    private static async Task<string> StartBackend(string name)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseKestrel();
                web.UseUrls("http://127.0.0.1:0");
                web.Configure(app => app.Run(ctx => ctx.Response.WriteAsync(name)));
            })
            .Build();
        await host.StartAsync();
        return GetUrl(host);
    }

    private static string GetUrl(IHost host)
    {
        var server = host.Services.GetRequiredService<IServer>();
        return server.Features.Get<IServerAddressesFeature>()!.Addresses.First();
    }

    private async Task<(HttpStatusCode Status, string Body)> Get(string path, string host)
    {
        using var client = new HttpClient { BaseAddress = new Uri(_proxyUrl) };
        client.DefaultRequestHeaders.Host = host;
        var resp = await client.GetAsync(path);
        return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ExactHost_BeatsWildcardHost()
    {
        var (status, body) = await Get("/api/orders", "api.example.com");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("exact", body);
    }

    [Fact]
    public async Task Host_MatchingIsCaseInsensitive()
    {
        // 配置里写的是 API.Example.COM，请求头用小写，仍然命中同一条路由
        var (status, body) = await Get("/api/orders", "api.example.com");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("exact", body);
    }

    [Fact]
    public async Task Path_LiteralsAreCaseInsensitive()
    {
        // 配置里路径首字母大写，请求用小写仍能命中——所以去重键必须把路径统一成小写
        var (status, body) = await Get("/API/orders", "api.example.com");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("exact", body);
    }

    [Fact]
    public async Task OtherSubdomain_FallsBackToWildcard()
    {
        var (status, body) = await Get("/anything", "shop.example.com");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("wild", body);
    }
}
