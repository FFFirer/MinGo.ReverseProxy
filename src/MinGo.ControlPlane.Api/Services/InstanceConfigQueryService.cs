using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MinGo.Core.Entities;
using MinGo.Core.Interfaces;
using MinGo.DataPlane.Grpc;

namespace MinGo.ControlPlane.Api.Services;

/// <summary>
/// 实例配置查询服务 - 通过 EventSubscription 通道向数据面发送 CONFIG_QUERY 并等待 CONFIG_REPORT
/// </summary>
public class InstanceConfigQueryService
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ConfigQueryResult>> _pendingQueries = new();
    private readonly IGatewayInstanceService _instanceService;
    private readonly ILogger<InstanceConfigQueryService> _logger;

    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(10);

    public InstanceConfigQueryService(
        IGatewayInstanceService instanceService,
        ILogger<InstanceConfigQueryService> logger)
    {
        _instanceService = instanceService;
        _logger = logger;
    }

    /// <summary>
    /// 查询指定实例的当前运行时配置
    /// </summary>
    public async Task<ConfigQueryResult> QueryConfigAsync(string instanceId, CancellationToken ct = default)
    {
        // 检查实例是否存在
        var instance = await _instanceService.GetInstanceAsync(instanceId);
        if (instance == null)
        {
            return ConfigQueryResult.CreateNotFound(instanceId);
        }

        var tcs = new TaskCompletionSource<ConfigQueryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var eventId = $"cq-{Guid.NewGuid():N}"[..20];

        if (!_pendingQueries.TryAdd(eventId, tcs))
        {
            return ConfigQueryResult.CreateError(instanceId, "Failed to register query");
        }

        try
        {
            // 发送 CONFIG_QUERY 事件（由 EventSubscriptionService 写入数据面流）
            var queryEvent = new EventMessage
            {
                EventId = eventId,
                Type = EventType.ConfigQuery,
                Source = "control-plane",
                DataJson = $"{{\"queryId\":\"{eventId}\"}}",
                TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            // EventSubscriptionService 会通过回调发送
            var sent = await TrySendEventAsync(instanceId, queryEvent);
            if (!sent)
            {
                return ConfigQueryResult.CreateOffline(instanceId);
            }

            // 等待响应或超时
            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(QueryTimeout, ct));
            if (completedTask == tcs.Task)
            {
                return await tcs.Task;
            }

            _logger.LogWarning("Config query {EventId} for instance {InstanceId} timed out", eventId, instanceId);
            return ConfigQueryResult.CreateTimeout(instanceId);
        }
        finally
        {
            _pendingQueries.TryRemove(eventId, out _);
        }
    }

    /// <summary>
    /// 处理来自数据面的 CONFIG_REPORT（由 EventSubscriptionService 调用）
    /// </summary>
    public void HandleConfigReport(string eventId, string dataJson)
    {
        if (_pendingQueries.TryRemove(eventId, out var tcs))
        {
            tcs.TrySetResult(ConfigQueryResult.CreateSuccess(dataJson));
            _logger.LogInformation("Config report received for query {EventId}", eventId);
        }
        else
        {
            _logger.LogWarning("No pending query found for event {EventId}", eventId);
        }
    }

    /// <summary>
    /// 发送事件到指定数据面（由外部注入发送逻辑）
    /// </summary>
    public Func<string, EventMessage, Task<bool>>? TrySendEventAsync { get; set; }
}

/// <summary>
/// 配置查询结果
/// </summary>
public class ConfigQueryResult
{
    public string InstanceId { get; private init; } = string.Empty;
    public bool IsSuccess { get; private init; }
    public string? DataJson { get; private init; }
    public string? ErrorMessage { get; private init; }
    public ConfigQueryFailureReason? FailureReason { get; private init; }

    public static ConfigQueryResult CreateSuccess(string dataJson) => new()
    {
        IsSuccess = true,
        DataJson = dataJson
    };

    public static ConfigQueryResult CreateNotFound(string instanceId) => new()
    {
        IsSuccess = false,
        InstanceId = instanceId,
        FailureReason = ConfigQueryFailureReason.NotFound,
        ErrorMessage = "Instance not found"
    };

    public static ConfigQueryResult CreateOffline(string instanceId) => new()
    {
        IsSuccess = false,
        InstanceId = instanceId,
        FailureReason = ConfigQueryFailureReason.Offline,
        ErrorMessage = "Instance is not connected"
    };

    public static ConfigQueryResult CreateTimeout(string instanceId) => new()
    {
        IsSuccess = false,
        InstanceId = instanceId,
        FailureReason = ConfigQueryFailureReason.Timeout,
        ErrorMessage = "Instance did not respond within timeout"
    };

    public static ConfigQueryResult CreateError(string instanceId, string message) => new()
    {
        IsSuccess = false,
        InstanceId = instanceId,
        FailureReason = ConfigQueryFailureReason.Error,
        ErrorMessage = message
    };
}

public enum ConfigQueryFailureReason
{
    NotFound,
    Offline,
    Timeout,
    Error
}
