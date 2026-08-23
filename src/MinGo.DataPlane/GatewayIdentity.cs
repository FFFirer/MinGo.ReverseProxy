namespace MinGo.DataPlane;

/// <summary>
/// 数据面实例身份 — 程序启动时即就绪，为所有服务提供统一的 DataPlaneId
/// </summary>
public class GatewayIdentity
{
    /// <summary>
    /// 数据面实例唯一标识，启动时生成，生命周期内不可变
    /// </summary>
    public string Id { get; }

    public GatewayIdentity()
    {
        Id = Guid.NewGuid().ToString("N")[..8];
    }
}
