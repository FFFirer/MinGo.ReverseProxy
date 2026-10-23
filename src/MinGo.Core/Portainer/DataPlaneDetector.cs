namespace MinGo.Core.Portainer;

/// <summary>
/// 判断目标网络上是否存在自身 stack 的 data-plane 容器。
/// 只有该条件成立时，容器名形态才可用（同网才能解析容器名）。
/// </summary>
public static class DataPlaneDetector
{
    public const string DefaultServiceName = "data-plane";

    public static bool IsAttached(
        IEnumerable<ContainerSnapshot> containers,
        string? networkName,
        SelfIdentity self,
        string? dataPlaneService)
    {
        if (!self.Detected || string.IsNullOrWhiteSpace(networkName))
        {
            return false;
        }

        var service = string.IsNullOrWhiteSpace(dataPlaneService)
            ? DefaultServiceName
            : dataPlaneService.Trim();

        return containers.Any(c =>
            c.IsRunning &&
            string.Equals(c.Service, service, StringComparison.OrdinalIgnoreCase) &&
            self.IsSelfStack(c.Stack) &&
            c.Networks.Any(n => string.Equals(n, networkName, StringComparison.Ordinal)));
    }
}
