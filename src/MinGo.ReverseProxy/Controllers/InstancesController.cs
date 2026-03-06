using Microsoft.AspNetCore.Mvc;
using MinGo.Core.Interfaces;
using MinGo.Core.Entities;
using MinGo.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MinGo.ReverseProxy.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InstancesController : ControllerBase
    {
        private readonly IGatewayInstanceService _gatewayInstanceService;

        public InstancesController(IGatewayInstanceService gatewayInstanceService)
        {
            _gatewayInstanceService = gatewayInstanceService;
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
        [HttpPost]
        public async Task<ActionResult<GatewayInstance>> RegisterInstance([FromBody] GatewayInstanceRegisterRequest request)
        {
            var registeredInstance = await _gatewayInstanceService.RegisterInstanceAsync(request);
            return CreatedAtAction(nameof(GetInstance), new { id = registeredInstance.InstanceId }, registeredInstance);
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
    }
}