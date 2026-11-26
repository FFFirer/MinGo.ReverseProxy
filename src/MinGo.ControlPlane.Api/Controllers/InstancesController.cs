using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MinGo.ControlPlane.Api.GrpcServices;
using MinGo.ControlPlane.Api.Services;
using MinGo.Core.Interfaces;
using MinGo.Core.Entities;
using MinGo.DataPlane.Grpc;

namespace MinGo.ControlPlane.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class InstancesController : ControllerBase
{
    private readonly IGatewayInstanceService _gatewayInstanceService;
    private readonly IApiDbService _apiDbService;
    private readonly InstanceConfigQueryService _configQueryService;
    private readonly DataPlaneConnectionManager _connectionManager;
    private readonly ConfigReplicationService _configReplication;
    private readonly ILogger<InstancesController> _logger;

    public InstancesController(
        IGatewayInstanceService gatewayInstanceService,
        IApiDbService apiDbService,
        InstanceConfigQueryService configQueryService,
        DataPlaneConnectionManager connectionManager,
        ConfigReplicationService configReplication,
        ILogger<InstancesController> logger)
    {
        _gatewayInstanceService = gatewayInstanceService;
        _apiDbService = apiDbService;
        _configQueryService = configQueryService;
        _connectionManager = connectionManager;
        _configReplication = configReplication;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<GatewayInstanceListResponse>> GetInstances()
    {
        var instances = await _gatewayInstanceService.GetInstancesAsync();
        return Ok(instances);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GatewayInstance>> GetInstance(string id)
    {
        var instance = await _gatewayInstanceService.GetInstanceAsync(id);
        if (instance == null) return NotFound();
        return Ok(instance);
    }

    [HttpPost("register")]
    public async Task<ActionResult<GatewayInstance>> RegisterInstance([FromBody] GatewayInstanceRegisterRequest request)
    {
        var instance = await _gatewayInstanceService.RegisterInstanceAsync(request);

        // 从落库的分配表解析节点归属（控制面重启后分配依然有效）
        var assignment = await _apiDbService.GetInstanceNodeAsync(instance.InstanceId);
        if (assignment != null)
            await _gatewayInstanceService.UpdateInstanceNodeAsync(instance.InstanceId, assignment.NodeId, assignment.NodeName);

        return CreatedAtAction(nameof(GetInstance), new { id = instance.InstanceId }, instance);
    }

    /// <summary>
    /// 绑定实例到节点（nodeId 为 null 表示解绑）。
    /// 绑定落库并同步内存态；实例在线时立即按新节点重推快照（解绑推空快照清规则）。
    /// </summary>
    [HttpPut("{id}/node")]
    public async Task<ActionResult<GatewayInstance>> SetInstanceNode(string id, [FromBody] SetInstanceNodeRequest? request)
    {
        var instance = await _gatewayInstanceService.GetInstanceAsync(id);
        if (instance == null)
            return NotFound(new { message = $"实例 {id} 不存在" });

        var nodeId = string.IsNullOrWhiteSpace(request?.NodeId) ? null : request.NodeId.Trim();
        string? nodeName = null;
        if (nodeId != null)
        {
            var node = await _apiDbService.GetNodeAsync(nodeId);
            if (node == null)
                return BadRequest(new { message = $"节点 {nodeId} 不存在" });
            nodeName = node.Name;
        }

        await _apiDbService.SetInstanceNodeAsync(id, nodeId);
        await _gatewayInstanceService.UpdateInstanceNodeAsync(id, nodeId, nodeName);
        _connectionManager.SetNode(id, nodeId);

        // 在线实例立即按新节点重推快照（解绑推空快照）；离线实例在下次注册时按新绑定组装
        await _configReplication.BroadcastConfigToDataPlaneAsync(id);

        var updated = await _gatewayInstanceService.GetInstanceAsync(id);
        return Ok(updated);
    }

    public class SetInstanceNodeRequest
    {
        public string? NodeId { get; set; }
    }

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] GatewayInstanceHeartbeatRequest? request)
    {
        if (request == null || string.IsNullOrEmpty(request.InstanceId))
            return BadRequest(new { message = "InstanceId is required" });

        var success = await _gatewayInstanceService.UpdateHeartbeatAsync(request);
        if (!success)
            return NotFound(new { message = $"Instance {request.InstanceId} not found" });

        return Ok(new { success = true });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteInstance(string id)
    {
        await _gatewayInstanceService.RemoveInstanceAsync(id);
        return NoContent();
    }

    /// <summary>
    /// 获取指定网关实例的当前 YARP 运行时配置
    /// </summary>
    [HttpGet("{id}/config")]
    public async Task<IActionResult> GetInstanceConfig(string id)
    {
        var result = await _configQueryService.QueryConfigAsync(id);

        if (!result.IsSuccess)
        {
            return result.FailureReason switch
            {
                ConfigQueryFailureReason.NotFound => NotFound(new { message = result.ErrorMessage }),
                ConfigQueryFailureReason.Offline => StatusCode(503, new { message = result.ErrorMessage }),
                ConfigQueryFailureReason.Timeout => StatusCode(504, new { message = result.ErrorMessage }),
                _ => StatusCode(500, new { message = result.ErrorMessage })
            };
        }

        // 解析 JSON 并返回
        try
        {
            var configDoc = JsonDocument.Parse(result.DataJson!);
            return Ok(configDoc.RootElement);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse config JSON for instance {InstanceId}", id);
            return StatusCode(500, new { message = "Invalid config data received from instance" });
        }
    }

    /// <summary>
    /// 通过心跳响应流向实例下发控制命令（reload-config / reload-certs / shutdown）。
    /// 命令经数据面的心跳双向流送达，要求实例在线且心跳通道已建立。
    /// </summary>
    [HttpPost("{id}/commands")]
    public async Task<IActionResult> SendCommand(string id, [FromBody] InstanceCommandRequest? request)
    {
        var commandType = request?.Type?.Trim().ToLowerInvariant() switch
        {
            "reload-config" or "reload_config" => CommandType.CmdReloadConfig,
            "reload-certs" or "reload_certs" => CommandType.CmdReloadCerts,
            "shutdown" => CommandType.CmdShutdown,
            _ => (CommandType?)null
        };

        if (commandType == null)
        {
            return BadRequest(new
            {
                message = "Unsupported command type. Allowed: reload-config, reload-certs, shutdown"
            });
        }

        var instance = await _gatewayInstanceService.GetInstanceAsync(id);
        if (instance == null)
            return NotFound(new { message = $"Instance {id} not found" });

        var sent = await _connectionManager.SendCommandAsync(id, commandType.Value, null);
        if (!sent)
        {
            return StatusCode(503, new
            {
                message = $"Instance {id} has no heartbeat channel (offline or never connected)"
            });
        }

        return Accepted(new { instanceId = id, command = request!.Type });
    }

    public class InstanceCommandRequest
    {
        public string Type { get; set; } = string.Empty;
    }
}
