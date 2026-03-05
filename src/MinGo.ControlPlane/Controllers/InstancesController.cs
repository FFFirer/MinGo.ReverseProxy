using Microsoft.AspNetCore.Mvc;
using MinGo.ControlPlane.Services;
using MinGo.Shared.Models;
using System.Net.Http;

namespace MinGo.ControlPlane.Controllers;

/// <summary>
/// Gateway实例管理控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class InstancesController : ControllerBase
{
    private readonly IGatewayInstanceService _instanceService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<InstancesController> _logger;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="instanceService">实例服务</param>
    /// <param name="httpClientFactory">HTTP 客户端工厂</param>
    /// <param name="logger">日志记录器</param>
    public InstancesController(IGatewayInstanceService instanceService, IHttpClientFactory httpClientFactory, ILogger<InstancesController> logger)
    {
        _instanceService = instanceService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// 获取所有Gateway实例
    /// </summary>
    /// <returns>实例列表</returns>
    [HttpGet]
    public async Task<ActionResult<GatewayInstanceListResponse>> GetInstances()
    {
        var result = await _instanceService.GetInstancesAsync();
        return Ok(result);
    }

    /// <summary>
    /// 获取指定Gateway实例
    /// </summary>
    /// <param name="instanceId">实例ID</param>
    /// <returns>实例信息</returns>
    [HttpGet("{instanceId}")]
    public async Task<ActionResult<GatewayInstance>> GetInstance(string instanceId)
    {
        var instance = await _instanceService.GetInstanceAsync(instanceId);
        if (instance == null)
        {
            return NotFound(new { message = $"Instance {instanceId} not found" });
        }
        return Ok(instance);
    }

    /// <summary>
    /// 更新Gateway实例心跳
    /// </summary>
    /// <param name="request">心跳请求</param>
    /// <returns>操作结果</returns>
    [HttpPost("heartbeat")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Heartbeat([FromBody] GatewayInstanceHeartbeatRequest? request)
    {
        if (request == null || string.IsNullOrEmpty(request.InstanceId))
        {
            return BadRequest(new { message = "InstanceId is required" });
        }

        var success = await _instanceService.UpdateHeartbeatAsync(request);
        if (!success)
        {
            return NotFound(new { message = $"Instance {request.InstanceId} not found" });
        }
        return Ok(new { success = true });
    }

    /// <summary>
    /// 注册Gateway实例
    /// </summary>
    /// <param name="request">注册请求</param>
    /// <returns>注册结果</returns>
    [HttpPost("register")]
    [IgnoreAntiforgeryToken]
    public async Task<ActionResult<GatewayInstance>> Register([FromBody] GatewayInstanceRegisterRequest? request)
    {
        if (request == null || string.IsNullOrEmpty(request.Name))
        {
            return BadRequest(new { message = "Instance name is required" });
        }

        var result = await _instanceService.RegisterInstanceAsync(request);
        return Ok(result);
    }

    /// <summary>
    /// 移除Gateway实例
    /// </summary>
    /// <param name="instanceId">实例ID</param>
    /// <returns>操作结果</returns>
    [HttpDelete("{instanceId}")]
    public async Task<IActionResult> RemoveInstance(string instanceId)
    {
        var success = await _instanceService.RemoveInstanceAsync(instanceId);
        if (!success)
        {
            return NotFound(new { message = $"Instance {instanceId} not found" });
        }
        return Ok(new { success = true });
    }

    /// <summary>
    /// 获取指定Gateway实例的当前YARP配置
    /// </summary>
    /// <param name="instanceId">实例ID</param>
    /// <returns>实例的当前YARP配置</returns>
    [HttpGet("{instanceId}/config")]
    public async Task<ActionResult<object>> GetInstanceConfig(string instanceId)
    {
        // 获取实例信息
        var instance = await _instanceService.GetInstanceAsync(instanceId);
        if (instance == null)
        {
            return NotFound(new { message = $"Instance {instanceId} not found" });
        }

        try
        {
            // 构建实例的配置 API 地址
            var configUrl = $"{instance.ListenerAddresses?.FirstOrDefault() ?? $"http://{instance.IpAddress}:{instance.Port}"}/api/config/current";
            _logger.LogInformation("正在从 {Url} 获取实例配置", configUrl);

            // 创建 HTTP 客户端并调用实例的配置 API
            var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetAsync(configUrl);

            if (response.IsSuccessStatusCode)
            {
                var config = await response.Content.ReadFromJsonAsync<object>();
                return Ok(config);
            }
            else
            {
                _logger.LogWarning("获取实例配置失败: {StatusCode}", response.StatusCode);
                return StatusCode((int)response.StatusCode, new { message = $"Failed to get instance config: {response.StatusCode}" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取实例配置异常");
            return StatusCode(500, new { message = "Failed to get instance config: " + ex.Message });
        }
    }
}
