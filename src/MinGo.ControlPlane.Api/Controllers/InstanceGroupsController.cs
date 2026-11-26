using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MinGo.ControlPlane.Api.GrpcServices;
using MinGo.Core.Entities;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;

namespace MinGo.ControlPlane.Api.Controllers;

/// <summary>
/// 数据面分组（接入通道资产）：路由按 <c>group:&lt;Id&gt;</c> 引用，
/// 实例在「实例管理」页分配分组；两处双向维护。
/// </summary>
[ApiController]
[Route("api/instance-groups")]
[Authorize]
public class InstanceGroupsController : ControllerBase
{
    private readonly IApiDbService _apiDbService;
    private readonly IGatewayInstanceService _instanceService;
    private readonly ConfigReplicationService _configReplication;
    private readonly ILogger<InstanceGroupsController> _logger;

    public InstanceGroupsController(
        IApiDbService apiDbService,
        IGatewayInstanceService instanceService,
        ConfigReplicationService configReplication,
        ILogger<InstanceGroupsController> logger)
    {
        _apiDbService = apiDbService;
        _instanceService = instanceService;
        _configReplication = configReplication;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DataPlaneGroup>>> GetGroups()
    {
        var groups = await _apiDbService.GetGroupsAsync();

        // 在线数来自内存注册表的运行时状态，由 API 层归并
        var onlineByGroup = (await _instanceService.GetInstancesAsync()).Instances
            .Where(i => i.Status == GatewayInstanceStatus.Online && !string.IsNullOrEmpty(i.GroupId))
            .GroupBy(i => i.GroupId)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var group in groups)
            group.OnlineCount = onlineByGroup.GetValueOrDefault(group.Id);

        return Ok(groups);
    }

    [HttpPost]
    public async Task<ActionResult<DataPlaneGroup>> CreateGroup([FromBody] GroupNameRequest? request)
    {
        var name = request?.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            return BadRequest(new { message = "分组名称不能为空" });

        var created = await _apiDbService.CreateGroupAsync(name);
        return CreatedAtAction(nameof(GetGroups), new { id = created.Id }, created);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<DataPlaneGroup>> RenameGroup(string id, [FromBody] GroupNameRequest? request)
    {
        var name = request?.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            return BadRequest(new { message = "分组名称不能为空" });

        var updated = await _apiDbService.RenameGroupAsync(id, name);
        if (updated == null) return NotFound(new { message = $"分组 {id} 不存在" });

        // 同步已分配实例的内存展示名（路由按 Id 引用，重命名不影响下发的规则集）
        await RefreshInMemoryGroupAsync(id, name);
        return Ok(updated);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteGroup(string id)
    {
        var result = await _apiDbService.DeleteGroupAsync(id);
        if (result == null)
            return NotFound(new { message = $"分组 {id} 不存在" });

        if (!result.Deleted)
        {
            // fail-closed：被路由引用时不级联删除，返回引用清单让运维先改路由
            return Conflict(new
            {
                message = $"分组仍被 {result.ReferencingRouteNames.Count} 条路由引用，请先调整这些路由的接入通道",
                routes = result.ReferencingRouteNames
            });
        }

        // 该分组下的实例回到未分配：清内存态并重推快照（分组路由不再下发）
        var affected = (await _instanceService.GetInstancesAsync()).Instances
            .Where(i => i.GroupId == id)
            .Select(i => i.InstanceId)
            .ToList();
        foreach (var instanceId in affected)
            await _instanceService.UpdateInstanceGroupAsync(instanceId, null, null);

        if (affected.Count > 0)
            await _configReplication.BroadcastConfigUpdateAsync();

        return NoContent();
    }

    private async Task RefreshInMemoryGroupAsync(string groupId, string newName)
    {
        var affected = (await _instanceService.GetInstancesAsync()).Instances
            .Where(i => i.GroupId == groupId)
            .Select(i => i.InstanceId)
            .ToList();
        foreach (var instanceId in affected)
            await _instanceService.UpdateInstanceGroupAsync(instanceId, groupId, newName);
    }

    public class GroupNameRequest
    {
        public string? Name { get; set; }
    }
}
