using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MinGo.ControlPlane.Api.GrpcServices;
using MinGo.Core.Entities;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;

namespace MinGo.ControlPlane.Api.Controllers;

/// <summary>
/// 数据面节点（接入通道资产）：路由按 <c>node:&lt;Id&gt;</c> 引用，
/// 实例在「实例管理」页绑定节点；两处双向维护。未绑定节点的实例不推送配置。
/// </summary>
[ApiController]
[Route("api/data-plane-nodes")]
[Authorize]
public class DataPlaneNodesController : ControllerBase
{
    private readonly IApiDbService _apiDbService;
    private readonly IGatewayInstanceService _instanceService;
    private readonly ConfigReplicationService _configReplication;
    private readonly ILogger<DataPlaneNodesController> _logger;

    public DataPlaneNodesController(
        IApiDbService apiDbService,
        IGatewayInstanceService instanceService,
        ConfigReplicationService configReplication,
        ILogger<DataPlaneNodesController> logger)
    {
        _apiDbService = apiDbService;
        _instanceService = instanceService;
        _configReplication = configReplication;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DataPlaneNode>>> GetNodes()
    {
        var nodes = await _apiDbService.GetNodesAsync();

        // 在线数来自内存注册表的运行时状态，由 API 层归并
        var onlineByNode = (await _instanceService.GetInstancesAsync()).Instances
            .Where(i => i.Status == GatewayInstanceStatus.Online && !string.IsNullOrEmpty(i.NodeId))
            .GroupBy(i => i.NodeId)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var node in nodes)
            node.OnlineCount = onlineByNode.GetValueOrDefault(node.Id);

        return Ok(nodes);
    }

    [HttpPost]
    public async Task<ActionResult<DataPlaneNode>> CreateNode([FromBody] NodeNameRequest? request)
    {
        var name = request?.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            return BadRequest(new { message = "节点名称不能为空" });

        var created = await _apiDbService.CreateNodeAsync(name);
        return CreatedAtAction(nameof(GetNodes), new { id = created.Id }, created);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<DataPlaneNode>> RenameNode(string id, [FromBody] NodeNameRequest? request)
    {
        var name = request?.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            return BadRequest(new { message = "节点名称不能为空" });

        var updated = await _apiDbService.RenameNodeAsync(id, name);
        if (updated == null) return NotFound(new { message = $"节点 {id} 不存在" });

        // 同步已绑定实例的内存展示名（路由按 Id 引用，重命名不影响下发的规则集）
        await RefreshInMemoryNodeAsync(id, name);
        return Ok(updated);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteNode(string id)
    {
        if (id == ConnectionIds.DefaultNode)
            return BadRequest(new { message = "默认节点不可删除" });

        var result = await _apiDbService.DeleteNodeAsync(id);
        if (result == null)
            return NotFound(new { message = $"节点 {id} 不存在" });

        if (!result.Deleted)
        {
            // fail-closed：被路由引用时不级联删除，返回引用清单让运维先改路由
            return Conflict(new
            {
                message = $"节点仍被 {result.ReferencingRouteNames.Count} 条路由引用，请先调整这些路由的接入通道",
                routes = result.ReferencingRouteNames
            });
        }

        // 该节点下的实例解除绑定：清内存态并重推快照（节点路由不再下发，解绑推空快照）
        var affected = (await _instanceService.GetInstancesAsync()).Instances
            .Where(i => i.NodeId == id)
            .Select(i => i.InstanceId)
            .ToList();
        foreach (var instanceId in affected)
        {
            await _instanceService.UpdateInstanceNodeAsync(instanceId, null, null);
            await _configReplication.BroadcastConfigToDataPlaneAsync(instanceId);
        }

        return NoContent();
    }

    private async Task RefreshInMemoryNodeAsync(string nodeId, string newName)
    {
        var affected = (await _instanceService.GetInstancesAsync()).Instances
            .Where(i => i.NodeId == nodeId)
            .Select(i => i.InstanceId)
            .ToList();
        foreach (var instanceId in affected)
            await _instanceService.UpdateInstanceNodeAsync(instanceId, nodeId, newName);
    }

    public class NodeNameRequest
    {
        public string? Name { get; set; }
    }
}
