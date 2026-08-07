using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Industrial.Gateway.Host.Infrastructure;

/// <summary>
/// Authorizes first-party bearer sessions against IAM's live effective-permission
/// endpoint before exposing the Gateway dashboard. This keeps Gateway administration
/// on the same IAM session as MES while remaining fail-closed when IAM is unavailable.
/// </summary>
internal static class GatewayIamPermissionAuthorizer
{
    internal const string ViewPermission = "Platform.Gateway.Route.View";
    internal const string EditPermission = "Platform.Gateway.Route.Edit";

    internal static async Task<bool> AuthorizeAsync(
        HttpContext context,
        string authority,
        CancellationToken cancellationToken = default)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return false;

        var token = GetValidatedBearerToken(context);
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var requiredPermission = RequiresViewOnly(context.Request)
            ? ViewPermission
            : EditPermission;

        try
        {
            var clients = context.RequestServices.GetRequiredService<IHttpClientFactory>();
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                authority.TrimEnd('/') + "/api/iam/users/me/permissions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await clients.CreateClient().SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return false;

            var snapshot = await response.Content.ReadFromJsonAsync<GatewayPermissionSnapshot>(
                cancellationToken: cancellationToken);
            return snapshot?.Permissions?.Any(permission =>
                string.Equals(permission, "*", StringComparison.OrdinalIgnoreCase)
                || string.Equals(permission, requiredPermission, StringComparison.OrdinalIgnoreCase)) == true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("Industrial.Gateway.Security")
                .LogWarning(ex,
                    "IAM Gateway permission evaluation failed for {Path}; denying request.",
                    context.Request.Path);
            return false;
        }
    }

    private static bool RequiresViewOnly(HttpRequest request)
        => HttpMethods.IsGet(request.Method)
           || HttpMethods.IsHead(request.Method)
           || HttpMethods.IsOptions(request.Method)
           // SignalR negotiate is a POST but establishes a read-only health stream.
           || request.Path.StartsWithSegments("/platform/hubs/health", StringComparison.OrdinalIgnoreCase);

    private static string? GetValidatedBearerToken(HttpContext context)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return authorization[7..].Trim();

        // Browser WebSocket upgrades cannot set Authorization. JwtBearer has already
        // validated this query token before the dashboard authorization callback runs.
        if (context.Request.Path.StartsWithSegments("/platform/hubs/health", StringComparison.OrdinalIgnoreCase))
        {
            var queryToken = context.Request.Query["access_token"].ToString();
            if (!string.IsNullOrWhiteSpace(queryToken))
                return queryToken;
        }

        return null;
    }

    private sealed record GatewayPermissionSnapshot(HashSet<string> Permissions);
}
