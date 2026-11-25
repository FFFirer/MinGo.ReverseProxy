namespace MinGo.DataPlane;

/// <summary>
/// 数据面实例身份 — 程序启动时即就绪，为所有服务提供统一的 DataPlaneId。
/// 身份解析顺序：Agent:Id 配置 &gt; Agent:StateFile 状态文件 &gt; 临时随机值。
/// 状态文件（容器内默认 /app/data/agent-id）保证重启后身份不变，
/// 与控制面实例注册表、遥测统计的连续性对齐。
/// </summary>
public class GatewayIdentity
{
    /// <summary>
    /// 数据面实例唯一标识，生命周期内不可变
    /// </summary>
    public string Id { get; }

    public GatewayIdentity(IConfiguration configuration, ILogger<GatewayIdentity> logger)
    {
        var configuredId = configuration["Agent:Id"];
        if (!string.IsNullOrWhiteSpace(configuredId))
        {
            Id = configuredId.Trim();
            return;
        }

        var stateFile = configuration["Agent:StateFile"] ?? "/app/data/agent-id";
        try
        {
            if (File.Exists(stateFile))
            {
                var existing = File.ReadAllText(stateFile).Trim();
                if (existing.Length > 0)
                {
                    Id = existing;
                    return;
                }
            }

            var generated = Guid.NewGuid().ToString("N")[..8];
            var directory = Path.GetDirectoryName(Path.GetFullPath(stateFile));
            if (directory != null)
                Directory.CreateDirectory(directory);
            File.WriteAllText(stateFile, generated);
            Id = generated;
            logger.LogInformation("Generated persistent agent id {AgentId} (state file {StateFile})", Id, stateFile);
        }
        catch (Exception ex)
        {
            Id = Guid.NewGuid().ToString("N")[..8];
            logger.LogWarning(ex,
                "Cannot persist agent id to {StateFile}, falling back to ephemeral id {AgentId}", stateFile, Id);
        }
    }
}
