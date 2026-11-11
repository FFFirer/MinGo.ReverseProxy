using Microsoft.Extensions.Configuration;

namespace MinGo.DataPlane.Tests;

/// <summary>
/// 测试用配置源：宿主启动前写入 YARP 路由/集群配置。
/// 原先是 ReverseProxyE2ETests 的私有嵌套类，被第二个 E2E 类需要时提取为共享实现，
/// 避免各测试各自复制一份"看起来一样"的配置桩
/// </summary>
internal sealed class InMemoryConfigurationSource : IConfigurationSource
{
    private readonly Dictionary<string, string?> _data = new(StringComparer.OrdinalIgnoreCase);

    public void Set(string key, string? value)
    {
        _data[key] = value;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder) => new InMemoryProvider(this);

    private sealed class InMemoryProvider : ConfigurationProvider
    {
        private readonly InMemoryConfigurationSource _source;

        public InMemoryProvider(InMemoryConfigurationSource source)
        {
            _source = source;
        }

        public override void Load()
        {
            Data = new Dictionary<string, string?>(_source._data.Where(kv => kv.Value != null),
                StringComparer.OrdinalIgnoreCase);
        }

        public override string? ToString() => "InMemoryTestConfig";
    }
}
