using Grpc.Core;
using Grpc.Core.Interceptors;

namespace MinGo.DataPlane;

/// <summary>
/// 为所有发往控制面的 gRPC 调用附加共享密钥 metadata（x-dp-secret）。
/// 未配置 ControlPlane:SharedSecret 时是透明直通，本地/单机部署行为不变；
/// 配置后控制面 fail-closed 校验，密钥不匹配将拒绝整个连接。
/// </summary>
public class SharedSecretInterceptor : Interceptor
{
    public const string SecretHeaderName = "x-dp-secret";

    private readonly string? _secret;

    public SharedSecretInterceptor(IConfiguration configuration)
    {
        var secret = configuration["ControlPlane:SharedSecret"];
        _secret = string.IsNullOrWhiteSpace(secret) ? null : secret.Trim();
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request, ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        => continuation(request, WithSecret(context));

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request, ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
        => continuation(request, WithSecret(context));

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
        => continuation(WithSecret(context));

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
        => continuation(WithSecret(context));

    private ClientInterceptorContext<TRequest, TResponse> WithSecret<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        if (_secret == null)
            return context;

        var headers = new Metadata { { SecretHeaderName, _secret } };
        var options = context.Options.WithHeaders(headers);
        return new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, options);
    }
}
