using Microsoft.AspNetCore.Mvc;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MinGo.ReverseProxy.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ApiManagementController : ControllerBase
    {
        private readonly IApiManagementService _apiManagementService;

        public ApiManagementController(IApiManagementService apiManagementService)
        {
            _apiManagementService = apiManagementService;
        }

        /// <summary>
        /// 获取所有路由
        /// </summary>
        [HttpGet("routes")]
        public async Task<ActionResult<IEnumerable<RouteConfig>>> GetRoutes()
        {
            var routes = await _apiManagementService.GetRoutesAsync();
            return Ok(routes);
        }

        /// <summary>
        /// 获取指定路由
        /// </summary>
        [HttpGet("routes/{id}")]
        public async Task<ActionResult<RouteConfig>> GetRoute(string id)
        {
            var route = await _apiManagementService.GetRouteAsync(id);
            if (route == null)
            {
                return NotFound();
            }
            return Ok(route);
        }

        /// <summary>
        /// 创建路由
        /// </summary>
        [HttpPost("routes")]
        public async Task<ActionResult<RouteConfig>> CreateRoute([FromBody] RouteConfig route)
        {
            var createdRoute = await _apiManagementService.CreateRouteAsync(route);
            return CreatedAtAction(nameof(GetRoute), new { id = createdRoute.Id }, createdRoute);
        }

        /// <summary>
        /// 更新路由
        /// </summary>
        [HttpPut("routes/{id}")]
        public async Task<ActionResult<RouteConfig>> UpdateRoute(string id, [FromBody] RouteConfig route)
        {
            var updatedRoute = await _apiManagementService.UpdateRouteAsync(id, route);
            if (updatedRoute == null)
            {
                return NotFound();
            }
            return Ok(updatedRoute);
        }

        /// <summary>
        /// 删除路由
        /// </summary>
        [HttpDelete("routes/{id}")]
        public async Task<ActionResult>
        DeleteRoute(string id)
        {
            await _apiManagementService.DeleteRouteAsync(id);
            return NoContent();
        }

        /// <summary>
        /// 获取所有集群
        /// </summary>
        [HttpGet("clusters")]
        public async Task<ActionResult<IEnumerable<ClusterConfig>>> GetClusters()
        {
            var clusters = await _apiManagementService.GetClustersAsync();
            return Ok(clusters);
        }

        /// <summary>
        /// 获取指定集群
        /// </summary>
        [HttpGet("clusters/{id}")]
        public async Task<ActionResult<ClusterConfig>> GetCluster(string id)
        {
            var cluster = await _apiManagementService.GetClusterAsync(id);
            if (cluster == null)
            {
                return NotFound();
            }
            return Ok(cluster);
        }

        /// <summary>
        /// 创建集群
        /// </summary>
        [HttpPost("clusters")]
        public async Task<ActionResult<ClusterConfig>> CreateCluster([FromBody] ClusterConfig cluster)
        {
            var createdCluster = await _apiManagementService.CreateClusterAsync(cluster);
            return CreatedAtAction(nameof(GetCluster), new { id = createdCluster.Id }, createdCluster);
        }

        /// <summary>
        /// 更新集群
        /// </summary>
        [HttpPut("clusters/{id}")]
        public async Task<ActionResult<ClusterConfig>> UpdateCluster(string id, [FromBody] ClusterConfig cluster)
        {
            var updatedCluster = await _apiManagementService.UpdateClusterAsync(id, cluster);
            if (updatedCluster == null)
            {
                return NotFound();
            }
            return Ok(updatedCluster);
        }

        /// <summary>
        /// 删除集群
        /// </summary>
        [HttpDelete("clusters/{id}")]
        public async Task<ActionResult>
        DeleteCluster(string id)
        {
            await _apiManagementService.DeleteClusterAsync(id);
            return NoContent();
        }
    }
}