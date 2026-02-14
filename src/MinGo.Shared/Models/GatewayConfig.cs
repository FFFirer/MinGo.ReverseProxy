namespace MinGo.Shared.Models;

public class GatewayConfig
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public Dictionary<string, RouteConfig> Routes { get; set; } = new();
    public Dictionary<string, ClusterConfig> Clusters { get; set; } = new();
    public SecurityConfig Security { get; set; } = new();
    public MonitoringConfig Monitoring { get; set; } = new();
}

public class RouteConfig
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ClusterId { get; set; } = string.Empty;
    public RouteMatch Match { get; set; } = new();
    public RouteTransforms Transforms { get; set; } = new();
    public bool Enabled { get; set; } = true;
}

public class RouteMatch
{
    public string Path { get; set; } = string.Empty;
    public string? Host { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
}

public class RouteTransforms
{
    public Dictionary<string, string>? PathPattern { get; set; }
    public Dictionary<string, string>? PathPrefix { get; set; }
}

public class ClusterConfig
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, DestinationConfig> Destinations { get; set; } = new();
    public string LoadBalancingPolicy { get; set; } = "RoundRobin";
    public HealthCheckConfig HealthCheck { get; set; } = new();
}

public class DestinationConfig
{
    public string Address { get; set; } = string.Empty;
    public bool Healthy { get; set; } = true;
}

public class HealthCheckConfig
{
    public HealthCheckActiveConfig Active { get; set; } = new();
    public HealthCheckPassiveConfig Passive { get; set; } = new();
}

public class HealthCheckActiveConfig
{
    public bool Enabled { get; set; } = true;
    public string Interval { get; set; } = "00:00:10";
    public string Timeout { get; set; } = "00:00:05";
    public string Path { get; set; } = "/health";
    public int[] ExpectedStatusCodes { get; set; } = new[] { 200 };
}

public class HealthCheckPassiveConfig
{
    public bool Enabled { get; set; } = true;
    public string Policy { get; set; } = "FailureRate";
    public string ReactivationPeriod { get; set; } = "00:01:00";
}

public class SecurityConfig
{
    public List<ApiKeyConfig> ApiKeys { get; set; } = new();
    public IpRulesConfig IpRules { get; set; } = new();
    public RateLimitConfig RateLimits { get; set; } = new();
}

public class ApiKeyConfig
{
    public string Key { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}

public class IpRulesConfig
{
    public List<string> Whitelist { get; set; } = new();
    public List<string> Blacklist { get; set; } = new();
}

public class RateLimitConfig
{
    public RateLimitGlobalConfig Global { get; set; } = new();
    public RateLimitPerApiKeyConfig PerApiKey { get; set; } = new();
}

public class RateLimitGlobalConfig
{
    public int RequestsPerSecond { get; set; } = 1000;
}

public class RateLimitPerApiKeyConfig
{
    public int RequestsPerSecond { get; set; } = 100;
}

public class MonitoringConfig
{
    public bool Enabled { get; set; } = true;
    public string MetricsEndpoint { get; set; } = "/metrics";
    public string LogLevel { get; set; } = "Information";
}
