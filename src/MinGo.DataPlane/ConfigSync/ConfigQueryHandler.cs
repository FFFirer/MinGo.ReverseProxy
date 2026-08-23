using Grpc.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MinGo.DataPlane.Grpc;

namespace MinGo.DataPlane.ConfigSync;

/// <summary>
/// 配置查询处理器 - 通过 EventSubscription 双向流响应控制面的 CONFIG_QUERY
/// </summary>
public class ConfigQueryHandler : BackgroundService
{
    private readonly EventSubscription.EventSubscriptionClient _client;
    private readonly DataPlaneConfigProvider _configProvider;
    private readonly GatewayIdentity _identity;
    private readonly ILogger<ConfigQueryHandler> _logger;

    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    public ConfigQueryHandler(
        EventSubscription.EventSubscriptionClient client,
        DataPlaneConfigProvider configProvider,
        GatewayIdentity identity,
        ILogger<ConfigQueryHandler> logger)
    {
        _client = client;
        _configProvider = configProvider;
        _identity = identity;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var call = _client.SubscribeEvents(cancellationToken: stoppingToken);

                await call.RequestStream.WriteAsync(new EventMessage
                {
                    EventId = Guid.NewGuid().ToString("N")[..12],
                    Type = EventType.InstanceStatusChange,
                    Source = _identity.Id,
                    DataJson = """{"status":"connected"}""",
                    TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });

                _logger.LogInformation("Event subscription established for data plane {DataPlaneId}", _identity.Id);

                await foreach (var eventMsg in call.ResponseStream.ReadAllAsync(stoppingToken))
                {
                    await HandleEventAsync(eventMsg, call, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Event subscription connection lost, retrying in {Delay}s...", ReconnectDelay.Seconds);
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
        }
    }

    private async Task HandleEventAsync(
        EventMessage eventMsg,
        AsyncDuplexStreamingCall<EventMessage, EventMessage> call,
        CancellationToken ct)
    {
        switch (eventMsg.Type)
        {
            case EventType.ConfigQuery:
                await HandleConfigQueryAsync(eventMsg, call, ct);
                break;

            default:
                _logger.LogDebug("Ignoring event type {EventType}: {EventId}", eventMsg.Type, eventMsg.EventId);
                break;
        }
    }

    private async Task HandleConfigQueryAsync(
        EventMessage queryMsg,
        AsyncDuplexStreamingCall<EventMessage, EventMessage> call,
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Handling CONFIG_QUERY {EventId} from {Source}", queryMsg.EventId, queryMsg.Source);

            var configJson = _configProvider.GetConfigSnapshotJson();

            var report = new EventMessage
            {
                EventId = queryMsg.EventId,
                Type = EventType.ConfigReport,
                Source = _identity.Id,
                DataJson = configJson,
                TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await call.RequestStream.WriteAsync(report, ct);
            _logger.LogInformation("CONFIG_REPORT sent for query {EventId}", queryMsg.EventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle CONFIG_QUERY {EventId}", queryMsg.EventId);
        }
    }
}
