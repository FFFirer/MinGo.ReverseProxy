using Microsoft.AspNetCore.Mvc;
using MinGo.Core.Transforms;

namespace MinGo.ControlPlane.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TransformsController : ControllerBase
{
    /// <summary>
    /// 获取所有支持的 transform 类型 schema，供前端动态渲染配置表单
    /// </summary>
    [HttpGet("schemas")]
    public ActionResult<IReadOnlyList<TransformSchema>> GetSchemas()
    {
        return Ok(TransformSchemaRegistry.Schemas);
    }
}
