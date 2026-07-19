using Grpc.Core;
using MinGo.Core.Entities;
using MinGo.Core.Interfaces;
using MinGo.DataPlane.Grpc;

namespace MinGo.ControlPlane.Api.GrpcServices;

/// <summary>
/// 心跳收集 gRPC 服务 - 接收数据面心跳和指标
/// </summary>
public class HeartbeatCollectService : HeartbeatCollect.HeartbeatCollectBase
{
    private readonly DataPlaneConnectionManager _connectionManager;
    private readonly IGatewayInstanceService _instanceService;
    private readonly ILogger<HeartbeatCollectService> _logger;

    public HeartbeatCollectService(
        DataPlaneConnectionManager connectionManager,
        IGatewayInstanceService instanceService,
        ILogger<HeartbeatCollectService> logger)
    {
        _connectionManager = connectionManager;
        _instanceService = instanceService;
        _logger = logger;
    }

    public override async Task ReportHeartbeat(
        IAsyncStreamReader<HeartbeatRequest> requestStream,
        IServerStreamWriter<HeartbeatResponse> responseStream,
        ServerCallContext context)
    {
        string? dataPlaneId = null;

        try
        {
            await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
            {
                dataPlaneId = request.DataPlaneId;

                // 更新连接管理器心跳
                _connectionManager.UpdateHeartbeat(request.DataPlaneId);

                // 更新实例服务（内存存储）
                await _instanceService.UpdateHeartbeatAsync(new GatewayInstanceHeartbeatRequest
                {
                    InstanceId = request.DataPlaneId,
                    CpuUsage = request.CpuUsage,
                    MemoryUsage = request.MemoryUsage,
                    TotalRequests = request.TotalRequests,
                    ErrorRequests = request.ErrorRequests,
                    IsHealthy = request.IsHealthy
                });

                // 记录指标日志
                _logger.LogDebug(
                    "Heartbeat from {DataPlaneId}: CPU={Cpu} MEM={Mem} Reqs={Total}({Err} err) Healthy={Healthy}",
                    request.DataPlaneId,
                    request.CpuUsage.ToString("F1"),
                    request.MemoryUsage.ToString("F1"),
                    request.TotalRequests,
                    request.ErrorRequests,
                    request.IsHealthy);

                // 发送确认响应
                var response = new HeartbeatResponse
                {
                    Ack = true,
                    Message = "ok"
                };

                await responseStream.WriteAsync(response);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常断开
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Heartbeat connection lost for data plane {DataPlaneId}", dataPlaneId);
        }
    }
}
