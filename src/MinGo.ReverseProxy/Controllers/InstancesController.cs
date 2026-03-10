using Microsoft.AspNetCore.Mvc;
using MinGo.Core.Interfaces;
using MinGo.Core.Entities;
using MinGo.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Net.Http;

namespace MinGo.ReverseProxy.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InstancesController : ControllerBase
    {
        private readonly IGatewayInstanceService _gatewayInstanceService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<InstancesController> _logger;

        public InstancesController(IGatewayInstanceService gatewayInstanceService, IHttpClientFactory httpClientFactory, ILogger<InstancesController> logger)
        {
            _gatewayInstanceService = gatewayInstanceService;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        /// <summary>
        /// 获取所有网关实例
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<GatewayInstanceListResponse>> GetInstances()
        {
            var instances = await _gatewayInstanceService.GetInstancesAsync();
            return Ok(instances);
        }

        /// <summary>
        /// 获取指定网关实例
        /// </summary>
        [HttpGet("{id}")]
        public async Task<ActionResult<GatewayInstance>> GetInstance(string id)
        {
            var instance = await _gatewayInstanceService.GetInstanceAsync(id);
            if (instance == null)
            {
                return NotFound();
            }
            return Ok(instance);
        }

        /// <summary>
        /// 注册网关实例
        /// </summary>
        [HttpPost("register")]
        public async Task<ActionResult<GatewayInstance>> RegisterInstance([FromBody] GatewayInstanceRegisterRequest request)
        {
            var registeredInstance = await _gatewayInstanceService.RegisterInstanceAsync(request);
            return CreatedAtAction(nameof(GetInstance), new { id = registeredInstance.InstanceId }, registeredInstance);
        }

        /// <summary>
        /// 更新网关实例心跳
        /// </summary>
        [HttpPost("heartbeat")]
        public async Task<IActionResult> Heartbeat([FromBody] GatewayInstanceHeartbeatRequest? request)
        {
            if (request == null || string.IsNullOrEmpty(request.InstanceId))
            {
                return BadRequest(new { message = "InstanceId is required" });
            }

            var success = await _gatewayInstanceService.UpdateHeartbeatAsync(request);
            if (!success)
            {
                return NotFound(new { message = $"Instance {request.InstanceId} not found" });
            }
            return Ok(new { success = true });
        }

        /// <summary>
        /// 删除网关实例
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteInstance(string id)
        {
            var result = await _gatewayInstanceService.RemoveInstanceAsync(id);
            if (!result)
            {
                return NotFound();
            }
            return NoContent();
        }

        /// <summary>
        /// 获取指定网关实例的当前YARP配置
        /// </summary>
        [HttpGet("{instanceId}/config")]
        public async Task<ActionResult<object>> GetInstanceConfig(string instanceId)
        {
            // 获取实例信息
            var instance = await _gatewayInstanceService.GetInstanceAsync(instanceId);
            if (instance == null)
            {
                return NotFound(new { message = $"Instance {instanceId} not found" });
            }

            try
            {
                // 构建实例的配置 API 地址
                string? baseUrl = instance.ListenerAddresses?.FirstOrDefault();
                if (string.IsNullOrEmpty(baseUrl))
                {
                    if (!string.IsNullOrEmpty(instance.IpAddress) && instance.Port > 0)
                    {
                        baseUrl = $"http://{instance.IpAddress}:{instance.Port}";
                    }
                    else
                    {
                        return BadRequest(new { message = "Instance has no valid listener address or IP/Port configuration" });
                    }
                }
                
                var configUrl = $"{baseUrl.TrimEnd('/')}/api/config/current";
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
}