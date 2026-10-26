namespace MinGo.Core.Interfaces;

/// <summary>
/// Portainer 实例管理服务接口。
/// </summary>
public interface IPortainerInstanceService
{
    Task<IEnumerable<PortainerInstanceDto>> GetAllAsync();

    Task<PortainerInstanceDto?> GetByIdAsync(string id);

    Task<PortainerInstanceDto?> GetWithApiKeyAsync(string id);

    Task<PortainerInstanceDto> CreateAsync(PortainerInstanceDto dto);

    Task<PortainerInstanceDto?> UpdateAsync(string id, PortainerInstanceDto dto);

    Task DeleteAsync(string id);
}

/// <summary>
/// Portainer 实例 DTO（前端交互用）。
/// ApiKey 在读取时脱敏，写入时接受明文。
/// </summary>
public sealed class PortainerInstanceDto
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// 列表/详情接口返回脱敏值（前4字符+***）；创建/更新接口接收完整值。
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 10;

    public int CacheSeconds { get; set; } = 30;

    public string? HostAddress { get; set; }

    public string DataPlaneService { get; set; } = "data-plane";

    public string[] SelfStackNames { get; set; } = [];

    public string[] SelfImagePatterns { get; set; } = ["reverseproxy-cp"];

    public string[] ExtraExcludedStacks { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
