using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using MinGo.Shared.Models;

namespace MinGo.Gateway.Services;

/// <summary>
/// Gateway实例注册服务
/// </summary>
public class GatewayInstanceRegistrationService : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GatewayInstanceRegistrationService> _logger;
    private readonly string _instanceId;
    private readonly string _instanceName;
    private readonly string _instanceVersion;
    private readonly string _controlPlaneUrl;
    private readonly TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(10);
    private long _totalRequests;
    private long _errorRequests;
    private long _lastTotalRequests;
    private long _lastErrorRequests;
    private static readonly Random _random = new Random();

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="httpClientFactory">HTTP客户端工厂</param>
    /// <param name="configuration">配置</param>
    /// <param name="logger">日志记录器</param>
    public GatewayInstanceRegistrationService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<GatewayInstanceRegistrationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;

        _instanceId = _configuration["Gateway:InstanceId"] ?? Guid.NewGuid().ToString("N");
        _instanceName = _configuration["Gateway:Name"] ?? "Gateway-Default";
        _instanceVersion = _configuration["Gateway:Version"] ?? "1.0.0";
        _controlPlaneUrl = _configuration["ControlPlane:Url"] ?? "http://localhost:5000";
    }

    /// <summary>
    /// 执行后台任务
    /// </summary>
    /// <param name="stoppingToken">取消令牌</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Gateway instance registration service started with InstanceId: {InstanceId}", _instanceId);

        await RegisterAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SendHeartbeatAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending heartbeat to control plane");
            }

            await Task.Delay(_heartbeatInterval, stoppingToken);
        }

        await UnregisterAsync();
        _logger.LogInformation("Gateway instance registration service stopped");
    }

    /// <summary>
    /// 注册实例
    /// </summary>
    private async Task RegisterAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var request = new GatewayInstanceRegisterRequest
            {
                InstanceId = _instanceId,
                Name = _instanceName,
                Version = _instanceVersion,
                IpAddress = GetLocalIpAddress(),
                Port = GetGatewayPort(),
                Metadata = new Dictionary<string, string>
                {
                    { "Environment", Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production" },
                    { "MachineName", Environment.MachineName }
                }
            };

            var response = await client.PostAsJsonAsync($"{_controlPlaneUrl}/api/instances/register", request);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Gateway instance registered successfully at {Address}:{Port}", request.IpAddress, request.Port);
            }
            else
            {
                _logger.LogWarning("Failed to register gateway instance: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register gateway instance");
        }
    }

    /// <summary>
    /// 发送心跳
    /// </summary>
    private async Task SendHeartbeatAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var request = new GatewayInstanceHeartbeatRequest
            {
                InstanceId = _instanceId,
                CpuUsage = GetCpuUsage(),
                MemoryUsage = GetMemoryUsage(),
                TotalRequests = _totalRequests,
                ErrorRequests = _errorRequests,
                IsHealthy = true,
                Metadata = new Dictionary<string, string>
                {
                    { "LastHeartbeatTime", DateTimeOffset.UtcNow.ToString("o") }
                }
            };

            var response = await client.PostAsJsonAsync($"{_controlPlaneUrl}/api/instances/heartbeat", request);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to send heartbeat: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending heartbeat to control plane");
        }
    }

    /// <summary>
    /// 注销实例
    /// </summary>
    private async Task UnregisterAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            await client.DeleteAsync($"{_controlPlaneUrl}/api/instances/{_instanceId}");
            _logger.LogInformation("Gateway instance unregistered");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unregister gateway instance");
        }
    }

    /// <summary>
    /// 获取本地IP地址
    /// </summary>
    private static string GetLocalIpAddress()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            var endPoint = socket.LocalEndPoint as IPEndPoint;
            return endPoint?.Address.ToString() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    /// <summary>
    /// 获取Gateway端口
    /// </summary>
    private int GetGatewayPort()
    {
        var url = _configuration["Kestrel:Endpoints:GatewayHttp:Url"] ?? "http://localhost:8080";
        var uri = new Uri(url);
        return uri.Port;
    }

    /// <summary>
    /// 获取CPU使用率（简化实现）
    /// </summary>
    private static double GetCpuUsage()
    {
        return _random.NextDouble() * 50;
    }

    /// <summary>
    /// 获取内存使用率（简化实现）
    /// </summary>
    private static double GetMemoryUsage()
    {
        return _random.NextDouble() * 80;
    }

    /// <summary>
    /// 增加请求计数
    /// </summary>
    /// <param name="isError">是否错误请求</param>
    public void IncrementRequestCount(bool isError = false)
    {
        Interlocked.Increment(ref _totalRequests);
        if (isError)
        {
            Interlocked.Increment(ref _errorRequests);
        }
    }
}
