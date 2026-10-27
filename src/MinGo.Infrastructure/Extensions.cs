using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MinGo.Infrastructure;

/// <summary>
/// HttpClient 扩展方法
/// </summary>
public static class HttpClientExtensions
{
    /// <summary>
    /// 从配置中注册命名的 HttpClient
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddNamedHttpClients(this IServiceCollection services, IConfiguration configuration)
    {
        var httpClientConfig = configuration.GetSection("HttpClient");
        var clientsConfig = httpClientConfig.GetSection("Clients");

        if (clientsConfig.Exists())
        {
            var clientSections = clientsConfig.GetChildren();
            foreach (var clientSection in clientSections)
            {
                var clientName = clientSection.Key;
                var baseAddress = clientSection.GetValue<string>("BaseAddress");
                var timeout = clientSection.GetValue<TimeSpan?>("Timeout") ?? TimeSpan.FromSeconds(30);

                if (!string.IsNullOrEmpty(baseAddress))
                {
                    services.AddHttpClient(clientName, client =>
                    {
                        client.BaseAddress = new Uri(baseAddress);
                        client.Timeout = timeout;
                    });
                }
            }
        }

        return services;
    }
}