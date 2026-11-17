using System.Text.Json;
using MinGo.ControlPlane.Api.Services;

namespace MinGo.ControlPlane.Api.Tests;

/// <summary>
/// Cloudflare configurations 响应结构测试：ingress 嵌套在 result.config 内。
/// </summary>
public class CloudflareConfigurationDtoTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void TunnelConfiguration_DeserializesIngressFromNestedConfig()
    {
        const string json = """
        {
          "account_id": "abc",
          "config": {
            "ingress": [
              { "hostname": "api.example.com", "service": "http://data-plane:8080", "originRequest": {} },
              { "service": "http_status:404" }
            ],
            "warp-routing": { "enabled": false }
          },
          "source": "cloudflare",
          "tunnel_id": "t1",
          "version": 3
        }
        """;

        var configuration = JsonSerializer.Deserialize<TunnelConfiguration>(json, Web);

        Assert.NotNull(configuration);
        Assert.NotNull(configuration!.Config);
        var ingress = configuration.Config!.Ingress;
        Assert.NotNull(ingress);
        Assert.Equal(2, ingress!.Length);
        Assert.Equal("api.example.com", ingress[0].Hostname);
        Assert.Null(ingress[1].Hostname);
        Assert.Equal("http_status:404", ingress[1].Service);
    }

    [Fact]
    public void TunnelConfiguration_ToleratesMissingConfig()
    {
        const string json = """{ "source": "cloudflare", "tunnel_id": "t1", "version": 0 }""";

        var configuration = JsonSerializer.Deserialize<TunnelConfiguration>(json, Web);

        Assert.NotNull(configuration);
        Assert.Null(configuration!.Config);
    }
}