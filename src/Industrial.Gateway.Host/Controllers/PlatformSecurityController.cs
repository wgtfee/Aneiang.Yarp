using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using System.Security.Claims;

namespace Industrial.Gateway.Host.Controllers;

/// <summary>
/// Security-center shell hosted by Gateway. IAM remains the credential authority;
/// successful IAM login can also establish a short-lived Gateway dashboard session.
/// Dashboard access is separately restricted by Gateway:Dashboard:AllowedUsers.
/// </summary>
[ApiExplorerSettings(IgnoreApi = true)]
[Route("platform/security")]
public sealed class PlatformSecurityController : Controller
{
    public const string DashboardCookieScheme = "GatewayDashboard";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public PlatformSecurityController(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    [HttpGet]
    public IActionResult Index() => View("~/Views/Platform/Security.cshtml");

    [HttpGet("sso")]
    public IActionResult Sso() => View("~/Views/Platform/SsoLogin.cshtml");

    [HttpGet("callback")]
    public IActionResult Callback() => View("~/Views/Platform/Security.cshtml");

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] GatewayLoginRequest request, CancellationToken cancellationToken)
    {
        var authority = _configuration["Iam:Authority"]?.TrimEnd('/') ?? "http://localhost:5100";
        using var upstream = await _httpClientFactory.CreateClient().PostAsJsonAsync(
            authority + "/account/login", request, cancellationToken);

        if (upstream.Headers.TryGetValues("Set-Cookie", out var cookies))
            foreach (var cookie in cookies) Response.Headers.Append("Set-Cookie", cookie);

        var content = await upstream.Content.ReadAsStringAsync(cancellationToken);
        if (upstream.IsSuccessStatusCode && IsDashboardOperator(request.UserName))
        {
            var identity = new ClaimsIdentity(DashboardCookieScheme, ClaimTypes.Name, ClaimTypes.Role);
            identity.AddClaim(new Claim(ClaimTypes.Name, request.UserName));
            identity.AddClaim(new Claim("identity_source", "Platform"));
            identity.AddClaim(new Claim("gateway_dashboard", "true"));
            await HttpContext.SignInAsync(
                DashboardCookieScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties
                {
                    IsPersistent = false,
                    AllowRefresh = false,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
                });
        }

        return new ContentResult
        {
            StatusCode = (int)upstream.StatusCode,
            ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/json",
            Content = content
        };
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(DashboardCookieScheme);
        return NoContent();
    }

    private bool IsDashboardOperator(string userName)
    {
        var allowedUsers = _configuration.GetSection("Gateway:Dashboard:AllowedUsers").Get<string[]>()
            ?? ["admin"];
        return allowedUsers.Any(x => string.Equals(x?.Trim(), userName?.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record GatewayLoginRequest(string UserName, string Password, string? Tenant = null);
