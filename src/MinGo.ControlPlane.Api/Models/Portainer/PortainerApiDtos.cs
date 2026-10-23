using System.Text.Json;

namespace MinGo.ControlPlane.Api.Models.Portainer;

// 原始 Portainer / Docker API 响应模型（仅发现流程使用）。

public sealed class PortainerSystemStatusDto
{
    public string? Version { get; set; }
}

public sealed class PortainerEndpointDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContainerEngine { get; set; }
    public int Status { get; set; }
}

public sealed class PortainerContainerDto
{
    public string Id { get; set; } = string.Empty;
    public string? Image { get; set; }
    public string[]? Names { get; set; }
    public string? State { get; set; }
    public Dictionary<string, string>? Labels { get; set; }
    public PortainerContainerNetworkSettingsDto? NetworkSettings { get; set; }
    public List<PortainerPortDto>? Ports { get; set; }
}

public sealed class PortainerContainerNetworkSettingsDto
{
    public Dictionary<string, JsonElement>? Networks { get; set; }
}

public sealed class PortainerPortDto
{
    public string? IP { get; set; }
    public int PrivatePort { get; set; }
    public int? PublicPort { get; set; }
    public string? Type { get; set; }
}

public sealed class PortainerNetworkDto
{
    public string Name { get; set; } = string.Empty;
    public string? Driver { get; set; }
}
