using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MinGo.ControlPlane.Api.Models.Topology;
using MinGo.ControlPlane.Api.Services;

namespace MinGo.ControlPlane.Api.Controllers;

/// <summary>
/// 网络拓扑只读视图：接入通道 → 路由 → 集群 → 目标端点，附带配置一致性体检。
/// </summary>
[ApiController]
[Route("api/topology")]
[Authorize]
public class TopologyController : ControllerBase
{
    private readonly TopologyService _topologyService;

    public TopologyController(TopologyService topologyService)
    {
        _topologyService = topologyService;
    }

    /// <summary>取当前配置对应的拓扑图。</summary>
    [HttpGet]
    public async Task<ActionResult<TopologyResponse>> Get(CancellationToken cancellationToken)
    {
        return Ok(await _topologyService.GetAsync(cancellationToken));
    }
}
