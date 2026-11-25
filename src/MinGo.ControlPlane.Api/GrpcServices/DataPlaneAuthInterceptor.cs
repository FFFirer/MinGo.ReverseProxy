using Grpc.Core;
using Grpc.Core.Interceptors;

namespace MinGo.ControlPlane.Api.GrpcServices;

/// <summary>
/// 数据面 gRPC 接入认证：校验调用 metadata 中的 x-dp-secret。
/// 未配置 DataPlane:SharedSecrets 时直通（本地/单机部署默认，行为不变）；
/// 配置后 fail-closed——缺少或不匹配密钥的连接一律拒绝。
/// 必须启用：配置快照携带证书私钥，5001 端口绝不能裸奔对外。
/// </summary>
public class DataPlaneAuthInterceptor : Interceptor
{
    public const string SecretHeaderName = "x-dp-secret";

    private readonly IReadOnlySet<string> _secrets;
    private readonly ILogger<DataPlaneAuthInterceptor> _logger;

    public DataPlaneAuthInterceptor(IConfiguration configuration, ILogger<DataPlaneAuthInterceptor> logger)
    {
        _logger = logger;
        var configured = configuration.GetSection("DataPlane:SharedSecrets").Get<string[]>() ?? [];
        _secrets = new HashSet<string>(
            configured.Where(s => !string.IsNullOrWhiteSpace(s)),
            StringComparer.Ordinal);

        if (_secrets.Count > 0)
            _logger.LogInformation("Data plane gRPC auth enabled ({Count} shared secret(s) configured)", _secrets.Count);
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
        where TRequest : class
        where TResponse : class
        => await GuardAsync(context, () => continuation(request, context)).ConfigureAwait(false);

    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream, ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
        => await GuardAsync(context, () => continuation(requestStream, context)).ConfigureAwait(false);

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
        => await GuardAsync(context, () => continuation(request, responseStream, context)).ConfigureAwait(false);

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream, IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context, DuplexStreamingServerMethod<TRequest, TResponse> continuation)
        => await GuardAsync(context, () => continuation(requestStream, responseStream, context)).ConfigureAwait(false);

    private async Task<TResponse> GuardAsync<TResponse>(ServerCallContext context, Func<Task<TResponse>> next)
    {
        if (_secrets.Count == 0)
            return await next().ConfigureAwait(false);

        var provided = context.RequestHeaders.GetValue(SecretHeaderName);
        if (provided != null && _secrets.Contains(provided))
            return await next().ConfigureAwait(false);

        _logger.LogWarning("Rejected unauthenticated data plane connection from {Peer}", context.Peer);
        throw new RpcException(new Status(StatusCode.PermissionDenied, "invalid or missing x-dp-secret"));
    }

    private async Task GuardAsync(ServerCallContext context, Func<Task> next)
    {
        if (_secrets.Count == 0)
        {
            await next().ConfigureAwait(false);
            return;
        }

        var provided = context.RequestHeaders.GetValue(SecretHeaderName);
        if (provided != null && _secrets.Contains(provided))
        {
            await next().ConfigureAwait(false);
            return;
        }

        _logger.LogWarning("Rejected unauthenticated data plane connection from {Peer}", context.Peer);
        throw new RpcException(new Status(StatusCode.PermissionDenied, "invalid or missing x-dp-secret"));
    }
}
