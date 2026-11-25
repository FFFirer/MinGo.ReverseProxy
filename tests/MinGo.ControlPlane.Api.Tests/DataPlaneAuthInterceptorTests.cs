using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MinGo.ControlPlane.Api.GrpcServices;
using MinGo.DataPlane.Grpc;
using Moq;

namespace MinGo.ControlPlane.Api.Tests;

/// <summary>
/// 数据面 gRPC 接入认证：fail-closed 校验 x-dp-secret。
/// 覆盖直通（未配置密钥）与拒绝（缺失/错误密钥）两条路径。
/// </summary>
public class DataPlaneAuthInterceptorTests
{
    private static IConfiguration SecretsConfig(params string[] secrets)
    {
        var dict = new Dictionary<string, string?>();
        for (var i = 0; i < secrets.Length; i++)
            dict[$"DataPlane:SharedSecrets:{i}"] = secrets[i];
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static DataPlaneAuthInterceptor CreateInterceptor(params string[] secrets) =>
        new(SecretsConfig(secrets), NullLogger<DataPlaneAuthInterceptor>.Instance);

    private static ServerCallContext CreateContext(string? secret)
    {
        var metadata = new Metadata();
        if (secret != null)
            metadata.Add(DataPlaneAuthInterceptor.SecretHeaderName, secret);
        return new TestServerCallContext(metadata);
    }

    [Fact]
    public async Task NoSecretsConfigured_PassesThrough()
    {
        var interceptor = CreateInterceptor();
        var called = false;

        await interceptor.DuplexStreamingServerHandler(
            new Mock<IAsyncStreamReader<ConfigSubscription>>().Object,
            new Mock<IServerStreamWriter<HeartbeatResponse>>().Object,
            CreateContext(null),
            (_, _, _) => { called = true; return Task.CompletedTask; });

        Assert.True(called);
    }

    [Fact]
    public async Task MatchingSecret_PassesThrough()
    {
        var interceptor = CreateInterceptor("s3cret-a", "s3cret-b");

        var called = false;
        await interceptor.DuplexStreamingServerHandler(
            new Mock<IAsyncStreamReader<ConfigSubscription>>().Object,
            new Mock<IServerStreamWriter<HeartbeatResponse>>().Object,
            CreateContext("s3cret-b"),
            (_, _, _) => { called = true; return Task.CompletedTask; });

        Assert.True(called);
    }

    [Fact]
    public async Task MissingSecret_RejectsWithPermissionDenied()
    {
        var interceptor = CreateInterceptor("s3cret-a");

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.DuplexStreamingServerHandler(
                new Mock<IAsyncStreamReader<ConfigSubscription>>().Object,
                new Mock<IServerStreamWriter<HeartbeatResponse>>().Object,
                CreateContext(null),
                (_, _, _) => Task.CompletedTask));

        Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
    }

    [Fact]
    public async Task WrongSecret_RejectsWithPermissionDenied()
    {
        var interceptor = CreateInterceptor("s3cret-a");

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.DuplexStreamingServerHandler(
                new Mock<IAsyncStreamReader<ConfigSubscription>>().Object,
                new Mock<IServerStreamWriter<HeartbeatResponse>>().Object,
                CreateContext("wrong"),
                (_, _, _) => Task.CompletedTask));

        Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
    }

    [Fact]
    public async Task UnaryHandler_EnforcesSecretToo()
    {
        var interceptor = CreateInterceptor("s3cret-a");

        var ok = await interceptor.UnaryServerHandler(
            new ConfigSubscription(),
            CreateContext("s3cret-a"),
            (_, _) => Task.FromResult(new ConfigSnapshot()));
        Assert.NotNull(ok);

        await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.UnaryServerHandler(
                new ConfigSubscription(),
                CreateContext(null),
                (_, _) => Task.FromResult(new ConfigSnapshot())));
    }

    /// <summary>
    /// ServerCallContext 的公开属性不可覆写（委托给受保护的 Core 属性），
    /// Moq 无法 Setup，故手写最小测试替身。
    /// </summary>
    private sealed class TestServerCallContext : ServerCallContext
    {
        private readonly Metadata _requestHeaders;

        public TestServerCallContext(Metadata requestHeaders) => _requestHeaders = requestHeaders;

        protected override Metadata RequestHeadersCore => _requestHeaders;

        protected override string MethodCore => "MinGo.DataPlane.Grpc.ConfigReplication/ReplicateConfig";

        protected override string HostCore => "localhost";

        protected override string PeerCore => "ipv4:127.0.0.1:12345";

        protected override DateTime DeadlineCore => DateTime.UtcNow.AddMinutes(1);

        protected override CancellationToken CancellationTokenCore => CancellationToken.None;

        protected override Metadata ResponseTrailersCore => new();

        protected override Status StatusCore { get; set; } = Status.DefaultSuccess;

        protected override WriteOptions? WriteOptionsCore { get; set; }

        protected override AuthContext AuthContextCore => throw new NotSupportedException();

        protected override IDictionary<object, object> UserStateCore => new Dictionary<object, object>();

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) =>
            throw new NotSupportedException();

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}
