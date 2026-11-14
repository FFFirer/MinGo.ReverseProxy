using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using MinGo.ControlPlane.Api.GrpcServices;
using MinGo.Core.Entities;
using MinGo.Core.Interfaces;
using MinGo.Core.Services;
using Moq;
using MinGo.DataPlane.Grpc;
using GrpcAccessLogEntry = MinGo.DataPlane.Grpc.AccessLogEntry;
using GrpcMetricPoint = MinGo.DataPlane.Grpc.MetricPoint;

namespace MinGo.ControlPlane.Api.Tests;

/// <summary>
/// 心跳 ingest 侧的字段贯通测试。
/// 阶段三把关联键与代理维度送过了进程边界，这里守住落地的那一半：
/// 控制面重建的条目必须与数据面发出的一字不差，且"没上报"不能被伪造成一个维度值。
/// </summary>
public class HeartbeatIngestAccessLogTests
{
    [Fact]
    public async Task ReportHeartbeat_PreservesCorrelationKeysAndProxyDimensions()
    {
        var store = new TelemetryStore();
        var request = new HeartbeatRequest
        {
            DataPlaneId = "dp-1",
            AccessLogs =
            {
                new GrpcAccessLogEntry
                {
                    TimestampUnixMs = 1_700_000_000_000,
                    Method = "GET",
                    Path = "/api/orders",
                    StatusCode = 200,
                    DurationMs = 12,
                    ClientIp = "10.0.0.9",
                    Route = "orders",
                    TraceId = "0af7651916cd43dd8448eb211c80319c",
                    SpanId = "b7ad6b7169203331",
                    Cluster = "orders-cluster",
                    Destination = "http://10.1.1.5:8080"
                }
            }
        };

        await IngestAsync(store, request);

        var log = Assert.Single(store.AccessLogs);
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", log.TraceId);
        Assert.Equal("b7ad6b7169203331", log.SpanId);
        Assert.Equal("orders-cluster", log.Cluster);
        Assert.Equal("http://10.1.1.5:8080", log.Destination);
        Assert.Equal("dp-1", log.InstanceId);

        // 时间戳走 unix 毫秒往返，任何时区换算错误都会在这里露出来
        Assert.Equal(
            DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000).UtcDateTime,
            log.Timestamp);
    }

    [Fact]
    public async Task ReportHeartbeat_KeepsUnreportedFieldsNull_NotTheCollectorSentinel()
    {
        var store = new TelemetryStore();

        // proto3 的 string 没有"未设置"，老数据面发来的是空串。
        // 归一成 null 才让下游把"没上报"和"量到了 none 这个维度"区分开
        var request = new HeartbeatRequest
        {
            DataPlaneId = "dp-old",
            AccessLogs =
            {
                new GrpcAccessLogEntry
                {
                    Method = "POST",
                    Path = "/api/legacy",
                    StatusCode = 502,
                    TraceId = "",
                    SpanId = "",
                    Cluster = "",
                    Destination = ""
                }
            }
        };

        await IngestAsync(store, request);

        var log = Assert.Single(store.AccessLogs);
        Assert.Null(log.TraceId);
        Assert.Null(log.SpanId);
        Assert.Null(log.Cluster);
        Assert.Null(log.Destination);
        Assert.NotEqual(GatewayNoneSentinel, log.Cluster);
    }

    [Fact]
    public async Task ReportHeartbeat_StampsInstanceIdOnIngestedMetricPoints()
    {
        var store = new TelemetryStore();
        // store 会顺带清掉超出保留期的点，用当前时间才测得到"写进去了"
        var timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var request = new HeartbeatRequest
        {
            DataPlaneId = "dp-2",
            Metrics =
            {
                new GrpcMetricPoint
                {
                    Name = "gateway.requests.total",
                    Value = 1,
                    TimestampUnixMs = timestampMs
                }
            }
        };

        await IngestAsync(store, request);

        var point = Assert.Single(store.Metrics["gateway.requests.total"]);
        Assert.Equal("dp-2", point.Tags["instance_id"]);
        Assert.Equal(timestampMs,
            new DateTimeOffset(point.Timestamp, TimeSpan.Zero).ToUnixTimeMilliseconds());

        // 序号由 store 在入队时盖。控制面不靠游标读取，但漏了它等于走了一条绕过契约的写入路径
        Assert.True(point.Sequence > 0);
    }

    [Fact]
    public async Task ReportHeartbeat_ContinuesAcrossFramesAndAcksEachOne()
    {
        var store = new TelemetryStore();
        var responses = new List<HeartbeatResponse>();

        var service = BuildService(store);
        var requests = new[]
        {
            Frame("dp-a", "GET /a"),
            Frame("dp-b", "GET /b")
        };

        await service.ReportHeartbeat(
            new FakeRequestStreamReader(requests),
            new FakeResponseStreamWriter<HeartbeatResponse>(responses),
            new Mock<ServerCallContext>().Object);

        Assert.Equal(2, responses.Count);
        Assert.All(responses, r => Assert.True(r.Ack));
        Assert.Equal(2, store.AccessLogs.Count);
        Assert.Equal(new[] { "dp-a", "dp-b" }, store.AccessLogs.Select(l => l.InstanceId));
    }

    [Fact]
    public async Task ReportHeartbeat_ForwardsHealthAndCountersToInstanceService()
    {
        var store = new TelemetryStore();
        var instances = new Mock<IGatewayInstanceService>(MockBehavior.Strict);
        GatewayInstanceHeartbeatRequest? seen = null;
        instances
            .Setup(s => s.UpdateHeartbeatAsync(It.IsAny<GatewayInstanceHeartbeatRequest>()))
            .Callback<GatewayInstanceHeartbeatRequest>(r => seen = r)
            .ReturnsAsync(true);

        var service = new HeartbeatCollectService(
            new DataPlaneConnectionManager(NullLogger<DataPlaneConnectionManager>.Instance),
            instances.Object,
            store,
            NullLogger<HeartbeatCollectService>.Instance);

        await service.ReportHeartbeat(
            new FakeRequestStreamReader(new[]
            {
                new HeartbeatRequest
                {
                    DataPlaneId = "dp-3",
                    CpuUsage = 37.5,
                    MemoryUsage = 512.25,
                    TotalRequests = 900,
                    ErrorRequests = 7,
                    IsHealthy = false
                }
            }),
            new FakeResponseStreamWriter<HeartbeatResponse>(new List<HeartbeatResponse>()),
            new Mock<ServerCallContext>().Object);

        instances.VerifyAll();
        Assert.NotNull(seen);
        Assert.Equal("dp-3", seen!.InstanceId);
        Assert.Equal(37.5, seen.CpuUsage);
        Assert.Equal(512.25, seen.MemoryUsage);
        Assert.Equal(900, seen.TotalRequests);
        Assert.Equal(7, seen.ErrorRequests);
        Assert.False(seen.IsHealthy);
    }

    private const string GatewayNoneSentinel = "none";

    private static HeartbeatRequest Frame(string dataPlaneId, string path) => new()
    {
        DataPlaneId = dataPlaneId,
        AccessLogs =
        {
            new GrpcAccessLogEntry
            {
                Method = "GET",
                Path = path,
                StatusCode = 200,
                TraceId = "trace-" + dataPlaneId
            }
        }
    };

    private static HeartbeatCollectService BuildService(TelemetryStore store)
    {
        var instances = new Mock<IGatewayInstanceService>();
        instances
            .Setup(s => s.UpdateHeartbeatAsync(It.IsAny<GatewayInstanceHeartbeatRequest>()))
            .ReturnsAsync(true);

        return new HeartbeatCollectService(
            new DataPlaneConnectionManager(NullLogger<DataPlaneConnectionManager>.Instance),
            instances.Object,
            store,
            NullLogger<HeartbeatCollectService>.Instance);
    }

    private static async Task IngestAsync(TelemetryStore store, HeartbeatRequest request)
    {
        var service = BuildService(store);
        var responses = new List<HeartbeatResponse>();

        await service.ReportHeartbeat(
            new FakeRequestStreamReader(new[] { request }),
            new FakeResponseStreamWriter<HeartbeatResponse>(responses),
            new Mock<ServerCallContext>().Object);

        Assert.Single(responses);
    }

    private sealed class FakeRequestStreamReader(HeartbeatRequest[] requests) : IAsyncStreamReader<HeartbeatRequest>
    {
        private int _index = -1;

        public HeartbeatRequest Current =>
            _index >= 0 && _index < requests.Length ? requests[_index] : new HeartbeatRequest();

        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            _index++;
            return Task.FromResult(_index < requests.Length);
        }
    }

    private sealed class FakeResponseStreamWriter<T>(List<T> written) : IServerStreamWriter<T>
    {
        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(T message)
        {
            written.Add(message);
            return Task.CompletedTask;
        }
    }
}
