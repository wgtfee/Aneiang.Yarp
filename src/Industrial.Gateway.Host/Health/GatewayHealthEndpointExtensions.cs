using Aneiang.Yarp.Storage;
using Industrial.Health;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Configuration;

namespace Industrial.Gateway.Host.Health;

/// <summary>
/// V0.7.1 three-layer health contract for the Gateway itself. The liveness
/// endpoint never touches storage; dependency and traffic endpoints are the
/// only endpoints allowed to probe the local SQLite store.
/// </summary>
public static class GatewayHealthEndpointExtensions
{
    public static void MapIndustrialHealth(this WebApplication app, string serviceName)
    {
        app.MapGet("/health/live", () =>
        {
            var now = DateTimeOffset.UtcNow;
            var application = new ApplicationHealth(ServiceStatus.Healthy, true, now);
            return Results.Ok(new
            {
                service = serviceName,
                instance = Environment.MachineName,
                status = ServiceStatus.Healthy,
                application,
                checkedAt = now
            });
        });

        app.MapGet("/health/dependencies", async (
            IDbConnectionFactory database,
            IProxyConfigProvider proxyConfig,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await EvaluateAsync(serviceName, database, proxyConfig, cancellationToken);
            return Results.Ok(snapshot);
        });

        app.MapGet("/health/traffic", async (
            IDbConnectionFactory database,
            IProxyConfigProvider proxyConfig,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await EvaluateAsync(serviceName, database, proxyConfig, cancellationToken);
            var traffic = HealthSnapshotEvaluator.ToTrafficHealth(snapshot);
            return Results.Json(traffic, statusCode: traffic.Status == TrafficStatus.Allowed ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        });
    }

    private static async Task<ServiceHealthSnapshot> EvaluateAsync(
        string serviceName,
        IDbConnectionFactory database,
        IProxyConfigProvider proxyConfig,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var dependencies = new List<DependencyHealthItem>
        {
            proxyConfig.GetConfig().Routes.Count > 0
                ? new("GatewayConfig", DependencyStatus.Healthy, DependencyCriticality.Critical)
                : new("GatewayConfig", DependencyStatus.Unhealthy, DependencyCriticality.Critical,
                    "CONFIG_INVALID", "No proxy routes are loaded", now, "Gateway cannot route requests")
        };

        try
        {
            await using var connection = await database.CreateConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);
            dependencies.Add(new("GatewayStore", DependencyStatus.Healthy, DependencyCriticality.Critical));
        }
        catch (Exception ex)
        {
            dependencies.Add(new(
                "GatewayStore",
                DependencyStatus.Unhealthy,
                DependencyCriticality.Critical,
                "SQL_CONNECTION_FAILED",
                ex.Message,
                now,
                "Gateway configuration and health history are unavailable"));
        }

        return HealthSnapshotEvaluator.Evaluate(serviceName, Environment.MachineName, dependencies, checkedAt: now);
    }
}
