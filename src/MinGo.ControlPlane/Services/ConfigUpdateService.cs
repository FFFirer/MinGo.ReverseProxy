using MinGo.ControlPlane.Data;
using MinGo.Shared.Models;

namespace MinGo.ControlPlane.Services;

/// <summary>
/// 配置更新服务
/// </summary>
public class ConfigUpdateService : IHostedService, IDisposable
{
    private readonly ILogger<ConfigUpdateService> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly HttpClient _httpClient;
    private IApiDbService? _apiDbService;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="serviceScopeFactory">服务作用域工厂</param>
    /// <param name="httpClient">HTTP客户端</param>
    public ConfigUpdateService(
        ILogger<ConfigUpdateService> logger,
        IServiceScopeFactory serviceScopeFactory,
        HttpClient httpClient)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
        _httpClient = httpClient;

        // 创建作用域并获取API数据库服务实例
        using var scope = _serviceScopeFactory.CreateScope();
        _apiDbService = scope.ServiceProvider.GetRequiredService<IApiDbService>();

        // 订阅路由和集群变更事件
        if (_apiDbService != null)
        {
            _apiDbService.RoutesChanged += OnRoutesChanged;
            _apiDbService.ClustersChanged += OnClustersChanged;
        }
    }

    /// <summary>
    /// 启动服务
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>任务</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ConfigUpdateService started");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 停止服务
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>任务</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ConfigUpdateService stopped");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 路由变更事件处理
    /// </summary>
    /// <returns>任务</returns>
    private async Task OnRoutesChanged()
    {
        _logger.LogInformation("Routes changed, updating gateway config");
        await UpdateGatewayConfigAsync();
    }

    /// <summary>
    /// 集群变更事件处理
    /// </summary>
    /// <returns>任务</returns>
    private async Task OnClustersChanged()
    {
        _logger.LogInformation("Clusters changed, updating gateway config");
        await UpdateGatewayConfigAsync();
    }

    /// <summary>
    /// 更新网关配置
    /// </summary>
    /// <returns>任务</returns>
    private async Task UpdateGatewayConfigAsync()
    {
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var apiDbService = scope.ServiceProvider.GetRequiredService<IApiDbService>();
            var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();

            // 获取最新的路由和集群配置
            var routes = await apiDbService.GetRoutesAsync();
            var clusters = await apiDbService.GetClustersAsync();

            // 生成新的网关配置
            var config = new GatewayConfig
            {
                Id = Guid.NewGuid().ToString(),
                Version = $"1.0.{DateTimeOffset.UtcNow.Ticks}",
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = "System",
                Routes = routes.ToDictionary(r => r.Id, r => r),
                Clusters = clusters.ToDictionary(c => c.Id, c => c),
                Security = new SecurityConfig(),
                Monitoring = new MonitoringConfig()
            };

            // 保存配置
            var savedConfig = await configService.CreateConfigAsync(config);
            _logger.LogInformation("Created new gateway config: {ConfigId}, Version: {Version}", savedConfig.Id, savedConfig.Version);

            // 通知API网关更新配置
            await NotifyGatewayConfigChangeAsync(savedConfig);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update gateway config");
        }
    }

    /// <summary>
    /// 通知API网关配置变更
    /// </summary>
    /// <param name="config">网关配置</param>
    /// <returns>任务</returns>
    private async Task NotifyGatewayConfigChangeAsync(GatewayConfig config)
    {
        try
        {
            // 这里假设API网关有一个端点来接收配置更新通知
            // 实际实现中，需要根据API网关的具体实现来调整
            var gatewayUrl = "http://localhost:5000/api/config/reload";
            var response = await _httpClient.PostAsJsonAsync(gatewayUrl, config);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully notified gateway of config change");
            }
            else
            {
                _logger.LogWarning("Failed to notify gateway: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to notify gateway of config change");
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        // 取消订阅事件
        if (_apiDbService != null)
        {
            _apiDbService.RoutesChanged -= OnRoutesChanged;
            _apiDbService.ClustersChanged -= OnClustersChanged;
        }
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
