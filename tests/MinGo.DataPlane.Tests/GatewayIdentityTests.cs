using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MinGo.DataPlane.Tests;

/// <summary>
/// 数据面身份解析：Agent:Id 配置优先、状态文件持久化兜底、
/// 持久化失败时降级临时随机值且不抛异常。
/// </summary>
public class GatewayIdentityTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static GatewayIdentity CreateIdentity(Dictionary<string, string?> values) =>
        new(Config(values), NullLogger<GatewayIdentity>.Instance);

    [Fact]
    public void ConfiguredIdTakesPrecedence_AndSkipsStateFile()
    {
        var stateFile = Path.Combine(Path.GetTempPath(), $"mingo-id-{Guid.NewGuid():N}", "agent-id");

        var identity = CreateIdentity(new()
        {
            ["Agent:Id"] = "agent-1",
            ["Agent:StateFile"] = stateFile
        });

        Assert.Equal("agent-1", identity.Id);
        Assert.False(File.Exists(stateFile));
    }

    [Fact]
    public void StateFile_PersistsIdAcrossRestarts()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mingo-id-{Guid.NewGuid():N}");
        try
        {
            var stateFile = Path.Combine(dir, "agent-id");

            var first = CreateIdentity(new() { ["Agent:StateFile"] = stateFile }).Id;
            var second = CreateIdentity(new() { ["Agent:StateFile"] = stateFile }).Id;

            Assert.Equal(first, second);
            Assert.True(File.Exists(stateFile));
            Assert.Equal(first, File.ReadAllText(stateFile).Trim());
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PreexistingStateFile_IsAdopted()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mingo-id-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            var stateFile = Path.Combine(dir, "agent-id");
            File.WriteAllText(stateFile, "legacy-agent-42\n");

            var identity = CreateIdentity(new() { ["Agent:StateFile"] = stateFile });

            Assert.Equal("legacy-agent-42", identity.Id);
            Assert.Equal("legacy-agent-42\n", File.ReadAllText(stateFile));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PersistenceFailure_FallsBackToEphemeralId()
    {
        // 状态文件路径的父级是一个普通文件 → Directory.CreateDirectory 必然失败
        var blocker = Path.Combine(Path.GetTempPath(), $"mingo-id-blocker-{Guid.NewGuid():N}");
        File.WriteAllText(blocker, "block");
        try
        {
            var stateFile = Path.Combine(blocker, "child", "agent-id");

            var identity = CreateIdentity(new() { ["Agent:StateFile"] = stateFile });

            Assert.False(string.IsNullOrWhiteSpace(identity.Id));
            Assert.Equal(8, identity.Id.Length);
            Assert.False(File.Exists(stateFile));
        }
        finally
        {
            File.Delete(blocker);
        }
    }
}
