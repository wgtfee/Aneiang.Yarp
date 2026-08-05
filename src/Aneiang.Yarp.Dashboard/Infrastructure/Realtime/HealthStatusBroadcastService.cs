using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Aneiang.Yarp.Storage;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Realtime;

/// <summary>
/// Records health transitions and publishes them to the health SignalR group.
/// It intentionally records transitions, not every probe, to keep the history useful
/// and bounded while preserving the frontend/backend role separation.
/// </summary>
internal sealed class HealthStatusBroadcastService : BackgroundService
{
    private readonly IDashboardClusterQueryService _clusterQuery;
    private readonly IServiceHealthHistoryRepository? _history;
    private readonly IHubContext<HealthHub> _hub;
    private readonly ILogger<HealthStatusBroadcastService> _logger;
    private readonly DashboardOptions _options;
    private readonly ConcurrentDictionary<string, string> _lastStates = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastCleanupUtc = DateTime.MinValue;

    public HealthStatusBroadcastService(
        IDashboardClusterQueryService clusterQuery,
        IServiceProvider services,
        IHubContext<HealthHub> hub,
        ILogger<HealthStatusBroadcastService> logger,
        IOptions<DashboardOptions> options)
    {
        _clusterQuery = clusterQuery;
        _history = services.GetService<IServiceHealthHistoryRepository>();
        _hub = hub;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CollectAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Health transition collection failed");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task CollectAsync(CancellationToken ct)
    {
        if (_history != null && DateTime.UtcNow - _lastCleanupUtc > TimeSpan.FromHours(1))
        {
            _lastCleanupUtc = DateTime.UtcNow;
            var days = Math.Clamp(_options.HealthHistoryRetentionDays, 1, 3650);
            await _history.DeleteOlderThanAsync(DateTime.UtcNow.AddDays(-days), ct);
        }

        foreach (var cluster in _clusterQuery.GetClusters())
        {
            var role = cluster.Metadata?.TryGetValue("healthRole", out var configuredRole) == true
                ? configuredRole : "backend";
            foreach (var destination in cluster.Destinations)
            {
                var status = string.IsNullOrWhiteSpace(destination.Health) ? "Unknown" : destination.Health!;
                var key = $"{cluster.ClusterId}/{destination.Name}";
                var isNew = _lastStates.TryAdd(key, status);
                var previous = isNew ? "Unknown" : _lastStates[key];
                if (isNew && string.Equals(status, "Unknown", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!isNew && string.Equals(previous, status, StringComparison.OrdinalIgnoreCase))
                    continue;

                _lastStates[key] = status;
                var change = new HealthStatusChange
                {
                    ClusterId = cluster.ClusterId,
                    DestinationId = destination.Name,
                    ServiceRole = role,
                    Status = status,
                    PreviousStatus = previous,
                    ActiveStatus = destination.ActiveHealth,
                    PassiveStatus = destination.PassiveHealth,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                if (_history != null)
                    await _history.SaveAsync(new ServiceHealthHistoryEntity
                    {
                        ClusterId = change.ClusterId,
                        DestinationId = change.DestinationId,
                        ServiceRole = change.ServiceRole,
                        Status = change.Status,
                        ActiveStatus = change.ActiveStatus,
                        PassiveStatus = change.PassiveStatus,
                        Reason = $"{previous} -> {status}",
                        ObservedAt = DateTime.UtcNow
                    }, ct);
                await _hub.Clients.Group("health").SendCoreAsync("HealthStatusChanged", new object[] { change }, ct);
            }
        }
    }
}

public sealed class HealthStatusChange
{
    [JsonPropertyName("clusterId")] public string ClusterId { get; set; } = string.Empty;
    [JsonPropertyName("destinationId")] public string DestinationId { get; set; } = string.Empty;
    [JsonPropertyName("serviceRole")] public string ServiceRole { get; set; } = "backend";
    [JsonPropertyName("status")] public string Status { get; set; } = "Unknown";
    [JsonPropertyName("previousStatus")] public string PreviousStatus { get; set; } = "Unknown";
    [JsonPropertyName("activeStatus")] public string? ActiveStatus { get; set; }
    [JsonPropertyName("passiveStatus")] public string? PassiveStatus { get; set; }
    [JsonPropertyName("timestamp")] public long Timestamp { get; set; }
}
