using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using System.Text.Json;
using Yarp.ReverseProxy;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;

/// <summary>Health check status and management API.</summary>
[Route("api/health-check")]
[ApiController]
public class HealthCheckController(DynamicYarpConfigService dynamicConfig, IProxyStateLookup proxyStateLookup, IHttpClientFactory httpClientFactory) : ControllerBase
{
    /// <summary>Get health check configuration for all clusters.</summary>
    [HttpGet("clusters")]
    public IActionResult GetClusterHealthConfigs()
    {
        var config = dynamicConfig.GetDynamicConfig();
        if (config == null)
            return Ok(new { code = 200, data = Array.Empty<object>() });

        var healthConfigs = config.Clusters.Select(c => new
        {
            clusterId = c.Config.ClusterId,
            healthCheck = c.HealthCheck,
            lastHeartbeat = c.LastHeartbeat
        }).ToList();

        return Ok(new { code = 200, data = healthConfigs });
    }

    /// <summary>Get passive health check status.</summary>
    [HttpGet("status")]
    public IActionResult GetHealthStatus()
    {
        var clusters = dynamicConfig.GetClusters();
        var status = clusters.Select(c => new
        {
            clusterId = c.ClusterId,
            healthCheck = c.HealthCheck != null ? new
            {
                active = c.HealthCheck.Active != null ? new
                {
                    enabled = c.HealthCheck.Active.Enabled ?? false,
                    path = c.HealthCheck.Active.Path,
                    interval = c.HealthCheck.Active.Interval?.ToString(),
                    timeout = c.HealthCheck.Active.Timeout?.ToString(),
                    policy = c.HealthCheck.Active.Policy
                } : null,
                passive = c.HealthCheck.Passive != null ? new
                {
                    enabled = c.HealthCheck.Passive.Enabled ?? false,
                    policy = c.HealthCheck.Passive.Policy,
                    reactivationPeriod = c.HealthCheck.Passive.ReactivationPeriod?.ToString()
                } : null,
                availableDestinationsPolicy = c.HealthCheck.AvailableDestinationsPolicy
            } : null,
            destinationCount = c.Destinations?.Count ?? 0
            , runtime = c.Destinations?.Select(d =>
            {
                proxyStateLookup.TryGetCluster(c.ClusterId ?? string.Empty, out var runtimeCluster);
                var runtimeDestination = runtimeCluster?.DestinationsState.AllDestinations.FirstOrDefault(x =>
                    string.Equals(x.DestinationId, d.Key, StringComparison.OrdinalIgnoreCase));
                return new
                {
                    destinationId = d.Key,
                    active = runtimeDestination?.Health.Active.ToString(),
                    passive = runtimeDestination?.Health.Passive.ToString()
                };
            })
        }).ToList();

        return Ok(new { code = 200, data = status });
    }

    /// <summary>Get V0.7.1 application/dependency/traffic details from every configured backend.</summary>
    [HttpGet("details")]
    public async Task<IActionResult> GetHealthDetails(CancellationToken cancellationToken)
    {
        var clusters = dynamicConfig.GetClusters();
        var client = httpClientFactory.CreateClient();
        // Some business health endpoints perform a short database/cache probe on
        // the first request after startup. Keep the probe timeout above that
        // warm-up window so a healthy service is not reported as unreachable.
        client.Timeout = TimeSpan.FromSeconds(10);
        var details = new List<object>();

        foreach (var cluster in clusters)
        {
            foreach (var destination in cluster.Destinations ?? new Dictionary<string, DestinationConfig>())
            {
                var address = destination.Value?.Address?.TrimEnd('/');
                var detail = new Dictionary<string, object?>
                {
                    ["clusterId"] = cluster.ClusterId,
                    ["destinationId"] = destination.Key,
                    ["address"] = address,
                    ["serviceStatus"] = "Unknown",
                    ["trafficStatus"] = "Blocked",
                    ["application"] = null,
                    ["dependencies"] = Array.Empty<object>(),
                    ["reasons"] = new[] { "HEALTH_ENDPOINT_UNREACHABLE" },
                    ["checkedAt"] = DateTimeOffset.UtcNow
                };

                var isFrontend = cluster.ClusterId?.EndsWith("-web", StringComparison.OrdinalIgnoreCase) == true;
                var probePath = isFrontend ? "/healthz" : "/health/dependencies";
                if (!string.IsNullOrWhiteSpace(address) && Uri.TryCreate(address + probePath, UriKind.Absolute, out var dependencyUri))
                {
                    var body = string.Empty;
                    try
                    {
                        using var response = await client.GetAsync(dependencyUri, cancellationToken);
                        body = await response.Content.ReadAsStringAsync(cancellationToken);
                        if (response.IsSuccessStatusCode || !string.IsNullOrWhiteSpace(body))
                        {
                            if (isFrontend)
                            {
                                detail["serviceStatus"] = response.IsSuccessStatusCode ? "Healthy" : "Unavailable";
                                detail["trafficStatus"] = response.IsSuccessStatusCode ? "Allowed" : "Blocked";
                                // Frontends may expose an HTML health page rather than JSON.
                                // HTTP 2xx is sufficient for the application-level status.
                                if (body.TrimStart().StartsWith("{"))
                                {
                                    using var frontendJson = JsonDocument.Parse(body);
                                    detail["application"] = frontendJson.RootElement.Clone();
                                    if (frontendJson.RootElement.TryGetProperty("checkedAt", out var frontendCheckedAt)) detail["checkedAt"] = frontendCheckedAt.GetString();
                                }
                                else detail["application"] = new { status = response.IsSuccessStatusCode ? "Healthy" : "Unavailable" };
                                detail["dependencies"] = Array.Empty<object>();
                                detail["reasons"] = response.IsSuccessStatusCode ? Array.Empty<string>() : new[] { "FRONTEND_HEALTHZ_FAILED" };
                            }
                            else
                            {
                                using var json = JsonDocument.Parse(body);
                                var root = json.RootElement;
                                if (root.TryGetProperty("serviceStatus", out var serviceStatus)) detail["serviceStatus"] = serviceStatus.GetString();
                                if (root.TryGetProperty("trafficStatus", out var trafficStatus)) detail["trafficStatus"] = trafficStatus.GetString();
                                if (root.TryGetProperty("application", out var application)) detail["application"] = application.Clone();
                                if (root.TryGetProperty("dependencies", out var dependencies)) detail["dependencies"] = dependencies.Clone();
                                if (root.TryGetProperty("reasons", out var reasons)) detail["reasons"] = reasons.Clone();
                                if (root.TryGetProperty("checkedAt", out var checkedAt)) detail["checkedAt"] = checkedAt.GetString();
                            }
                        }
                    }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
                    {
                        detail["reasons"] = new[] { "HEALTH_ENDPOINT_UNREACHABLE" };
                    }
                }

                details.Add(detail);
            }
        }

        return Ok(new { code = 200, data = details });
    }
}
