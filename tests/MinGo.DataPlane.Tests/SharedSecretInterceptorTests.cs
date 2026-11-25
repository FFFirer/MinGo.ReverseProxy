using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Configuration;
using MinGo.DataPlane.Grpc;

namespace MinGo.DataPlane.Tests;

/// <summary>
/// 数据面侧密钥注入拦截器：配置了 ControlPlane:SharedSecret 时向所有
/// 控制面调用附加 x-dp-secret；未配置时上下文原样直通。
/// </summary>
public class SharedSecretInterceptorTests
{
    private static ClientInterceptorContext<ConfigSubscription, ConfigSnapshot> CreateContext(CallOptions options)
    {
        var method = new Method<ConfigSubscription, ConfigSnapshot>(
            MethodType.DuplexStreaming,
            "MinGo.DataPlane.Grpc.ConfigReplication",
            "ReplicateConfig",
            Marshallers.Create((ConfigSubscription _) => Array.Empty<byte>(), _ => new ConfigSubscription()),
            Marshallers.Create((ConfigSnapshot _) => Array.Empty<byte>(), _ => new ConfigSnapshot()));
        return new ClientInterceptorContext<ConfigSubscription, ConfigSnapshot>(method, "localhost:5001", options);
    }

    private static SharedSecretInterceptor CreateInterceptor(string? secret)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ControlPlane:SharedSecret"] = secret
            })
            .Build();
        return new SharedSecretInterceptor(config);
    }

    [Fact]
    public void WithoutSecret_ContextPassesThroughUnchanged()
    {
        var interceptor = CreateInterceptor(null);
        ClientInterceptorContext<ConfigSubscription, ConfigSnapshot>? captured = null;

        interceptor.AsyncDuplexStreamingCall(
            CreateContext(default),
            ctx => { captured = ctx; return null!; });

        Assert.NotNull(captured);
        Assert.Null(captured!.Value.Options.Headers);
    }

    [Fact]
    public void WithSecret_AddsHeaderToDuplexCalls()
    {
        var interceptor = CreateInterceptor("s3cret");
        ClientInterceptorContext<ConfigSubscription, ConfigSnapshot>? captured = null;

        interceptor.AsyncDuplexStreamingCall(
            CreateContext(default),
            ctx => { captured = ctx; return null!; });

        Assert.NotNull(captured);
        Assert.Equal("s3cret", captured!.Value.Options.Headers.GetValue(SharedSecretInterceptor.SecretHeaderName));
    }

    [Fact]
    public void WithSecret_AddsHeaderToUnaryCalls()
    {
        var interceptor = CreateInterceptor("s3cret");
        ClientInterceptorContext<ConfigSubscription, ConfigSnapshot>? captured = null;

        interceptor.AsyncUnaryCall(
            new ConfigSubscription(),
            CreateContext(default),
            (request, ctx) => { captured = ctx; return DummyUnaryCall(); });

        Assert.NotNull(captured);
        Assert.Equal("s3cret", captured!.Value.Options.Headers.GetValue(SharedSecretInterceptor.SecretHeaderName));
    }

    [Fact]
    public void SecretIsTrimmed()
    {
        var interceptor = CreateInterceptor("  s3cret  ");
        ClientInterceptorContext<ConfigSubscription, ConfigSnapshot>? captured = null;

        interceptor.AsyncDuplexStreamingCall(
            CreateContext(default),
            ctx => { captured = ctx; return null!; });

        Assert.Equal("s3cret", captured!.Value.Options.Headers.GetValue(SharedSecretInterceptor.SecretHeaderName));
    }

    private static AsyncUnaryCall<ConfigSnapshot> DummyUnaryCall() => new(
        Task.FromResult(new ConfigSnapshot()),
        Task.FromResult(new Metadata()),
        () => Status.DefaultSuccess,
        () => new Metadata(),
        () => { });
}
