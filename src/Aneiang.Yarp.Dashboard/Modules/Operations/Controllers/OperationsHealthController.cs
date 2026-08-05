using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Aneiang.Yarp.Storage;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Modules.Operations.Controllers;

/// <summary>
/// Health check and system snapshot endpoints.
/// </summary>
[Route("api/operations")]
[ApiController]
public class OperationsHealthController : ControllerBase
{
    private readonly IDashboardClusterQueryService _clusterQuery;
    private readonly IDashboardRouteQueryService _routeQuery;
    private readonly IServiceHealthHistoryRepository _history;

    public OperationsHealthController(
        IDashboardClusterQueryService clusterQuery,
        IDashboardRouteQueryService routeQuery,
        IServiceHealthHistoryRepository history)
    {
        _clusterQuery = clusterQuery;
        _routeQuery = routeQuery;
        _history = history;
    }

    [HttpGet("health-summary")]
    public IActionResult GetHealthSummary()
    {
        var clusters = _clusterQuery.GetClusters();
        var totalDestinations = 0;
        var healthyDestinations = 0;
        var unhealthyDestinations = 0;
        var unknownDestinations = 0;
        var roles = new Dictionary<string, RoleHealth>(StringComparer.OrdinalIgnoreCase);

        foreach (var cluster in clusters)
        {
            if (cluster.Destinations != null)
            {
                foreach (var dest in cluster.Destinations)
                {
                    totalDestinations++;
                    var health = dest.Health;
                    var role = cluster.Metadata?.TryGetValue("healthRole", out var configuredRole) == true
                        ? configuredRole : "backend";
                    if (!roles.TryGetValue(role, out var roleHealth))
                        roles[role] = roleHealth = new RoleHealth();
                    roleHealth.Total++;
                    if (string.IsNullOrEmpty(health))
                    { roleHealth.Unknown++;
                        unknownDestinations++;
                    }
                    else if (health.Equals("Healthy", StringComparison.OrdinalIgnoreCase))
                    { roleHealth.Healthy++;
                        healthyDestinations++;
                    }
                    else if (health.Equals("Unhealthy", StringComparison.OrdinalIgnoreCase))
                    { roleHealth.Unhealthy++;
                        unhealthyDestinations++;
                    }
                    else
                    { roleHealth.Unknown++;
                        unknownDestinations++;
                    }
                }
            }
        }

        var healthScore = totalDestinations > 0
            ? Math.Round((double)healthyDestinations / totalDestinations * 100, 1)
            : 100;

        var data = new HealthSummaryData
        {
            HealthScore = healthScore,
            TotalClusters = clusters.Count,
            TotalDestinations = totalDestinations,
            HealthyCount = healthyDestinations,
            UnhealthyCount = unhealthyDestinations,
            UnknownCount = unknownDestinations,
            Status = healthScore >= 90 ? "Healthy" : healthScore >= 70 ? "Warning" : "Critical"
            , ByRole = roles
        };

        return Ok(new { code = 200, data });
    }

    [HttpGet("snapshot")]
    public IActionResult ExportSnapshot()
    {
        var clusters = _clusterQuery.GetClusters();
        var snapshot = new SystemSnapshot
        {
            ExportedAt = DateTime.Now,
            ClusterCount = clusters.Count,
            RouteCount = _routeQuery.GetRoutes().Count,
            Clusters = clusters.Select(c => new ClusterSnapshot
            {
                Id = c.ClusterId,
                DestinationCount = c.Destinations?.Count ?? 0
            }).ToList(),
            Routes = _routeQuery.GetRoutes().Select(r => new RouteSnapshot
            {
                Id = r.RouteId,
                ClusterId = r.ClusterId
            }).ToList()
        };

        return Ok(new { code = 200, data = snapshot });
    }

    [HttpGet("health-history")]
    public async Task<IActionResult> GetHealthHistory(
        [FromQuery] string? clusterId = null,
        [FromQuery] string? destinationId = null,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        var entries = await _history.ListAsync(clusterId, destinationId, limit, ct);
        return Ok(new { code = 200, data = entries });
    }

    private class HealthSummaryData
    {
        public double HealthScore { get; set; }
        public int TotalClusters { get; set; }
        public int TotalDestinations { get; set; }
        public int HealthyCount { get; set; }
        public int UnhealthyCount { get; set; }
        public int UnknownCount { get; set; }
        public string Status { get; set; } = "Healthy";
        public Dictionary<string, RoleHealth> ByRole { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class RoleHealth
    {
        public int Total { get; set; }
        public int Healthy { get; set; }
        public int Unhealthy { get; set; }
        public int Unknown { get; set; }
    }

    private class SystemSnapshot
    {
        public DateTime ExportedAt { get; set; }
        public int ClusterCount { get; set; }
        public int RouteCount { get; set; }
        public List<ClusterSnapshot> Clusters { get; set; } = new();
        public List<RouteSnapshot> Routes { get; set; } = new();
    }

    private class ClusterSnapshot
    {
        public string Id { get; set; } = string.Empty;
        public int DestinationCount { get; set; }
    }

    private class RouteSnapshot
    {
        public string Id { get; set; } = string.Empty;
        public string? ClusterId { get; set; }
    }
}
