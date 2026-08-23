using Grpc.Core;
using MinGo.Core.Entities;
using MinGo.Core.Interfaces;
using MinGo.Core.Services;
using MinGo.DataPlane.Grpc;
using CoreMetricPoint = MinGo.Core.Services.MetricPoint;
using CoreAccessLogEntry = MinGo.Core.Services.AccessLogEntry;

namespace MinGo.ControlPlane.Api.GrpcServices;

/// <summary>
/// 心跳收集 gRPC 服务 - 接收数据面心跳和指标
/// </summary>
public class HeartbeatCollectService : HeartbeatCollect.HeartbeatCollectBase
{
    private readonly DataPlaneConnectionManager _connectionManager;
    private readonly IGatewayInstanceService _instanceService;
    private readonly TelemetryStore _telemetryStore;
    private readonly ILogger<HeartbeatCollectService> _logger;

    public HeartbeatCollectService(
        DataPlaneConnectionManager connectionManager,
        IGatewayInstanceService instanceService,
        TelemetryStore telemetryStore,
        ILogger<HeartbeatCollectService> logger)
    {
        _connectionManager = connectionManager;
        _instanceService = instanceService;
        _telemetryStore = telemetryStore;
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

                // 写入遥测指标到 TelemetryStore
                foreach (var grpcPoint in request.Metrics)
                {
                    var tags = new Dictionary<string, object>();
                    foreach (var (key, val) in grpcPoint.Tags)
                    {
                        tags[key] = val;
                    }
                    // 标记数据来源
                    tags["instance_id"] = request.DataPlaneId;

                    _telemetryStore.AddMetric(grpcPoint.Name, new CoreMetricPoint
                    {
                        Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(grpcPoint.TimestampUnixMs).UtcDateTime,
                        Value = grpcPoint.Value,
                        Tags = tags
                    });
                }

                // 写入访问日志到 TelemetryStore
                foreach (var grpcLog in request.AccessLogs)
                {
                    _telemetryStore.AddAccessLog(new CoreAccessLogEntry
                    {
                        Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(grpcLog.TimestampUnixMs).UtcDateTime,
                        Method = grpcLog.Method,
                        Path = grpcLog.Path,
                        StatusCode = grpcLog.StatusCode,
                        DurationMs = grpcLog.DurationMs,
                        ClientIp = grpcLog.ClientIp,
                        Route = grpcLog.Route,
                        InstanceId = request.DataPlaneId
                    });
                }

                // 记录指标日志
                _logger.LogDebug(
                    "Heartbeat from {DataPlaneId}: CPU={Cpu} MEM={Mem} Reqs={Total}({Err} err) Healthy={Healthy} Metrics={MetricCount} Logs={LogCount}",
                    request.DataPlaneId,
                    request.CpuUsage.ToString("F1"),
                    request.MemoryUsage.ToString("F1"),
                    request.TotalRequests,
                    request.ErrorRequests,
                    request.IsHealthy,
                    request.Metrics.Count,
                    request.AccessLogs.Count);

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
