namespace MinGo.Core.Portainer;

/// <summary>
/// 从可导入列表中剔除自身 stack、Portainer 系统容器、非运行容器和额外排除项。
/// </summary>
public static class SelfExclusionPolicy
{
    public const string ReasonNotRunning = "not-running";
    public const string ReasonPortainerSystem = "portainer-system";
    public const string ReasonSelfStack = "self-stack";
    public const string ReasonExtraExcluded = "extra-excluded";

    public static ExclusionResult Apply(
        IEnumerable<ContainerSnapshot> containers,
        SelfIdentity self,
        IEnumerable<string>? extraExcludedStacks,
        string? endpoint = null)
    {
        var extra = Normalize(extraExcludedStacks);
        var included = new List<ContainerSnapshot>();
        var excluded = new List<ExcludedContainer>();

        foreach (var container in containers)
        {
            var reason = Classify(container, self, extra);
            if (reason is null)
            {
                included.Add(container);
            }
            else
            {
                excluded.Add(new ExcludedContainer(container.Name, reason, container.Stack, endpoint));
            }
        }

        return new ExclusionResult(included, excluded);
    }

    private static string? Classify(
        ContainerSnapshot container,
        SelfIdentity self,
        IReadOnlyCollection<string> extra)
    {
        if (!container.IsRunning) return ReasonNotRunning;
        if (container.IsPortainerSystem) return ReasonPortainerSystem;
        if (self.Detected && self.IsSelfStack(container.Stack)) return ReasonSelfStack;
        if (container.Stack is not null &&
            extra.Contains(container.Stack, StringComparer.OrdinalIgnoreCase))
        {
            return ReasonExtraExcluded;
        }

        return null;
    }

    private static List<string> Normalize(IEnumerable<string>? values)
    {
        if (values is null) return [];

        var result = new List<string>();
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            var trimmed = value.Trim();
            if (!result.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(trimmed);
            }
        }

        return result;
    }
}
