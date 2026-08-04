using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using System.Text.Json;

namespace Industrial.Gateway.Host.Controllers;

/// <summary>
/// S17 security-center shell. The page is hosted by Gateway, while all data is
/// retrieved from IAM through the Gateway API surface.
/// </summary>
[ApiExplorerSettings(IgnoreApi = true)]
[Route("platform/security")]
public sealed class PlatformSecurityController : Controller
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public PlatformSecurityController(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    [HttpGet]
    public IActionResult Index() => View("~/Views/Platform/Security.cshtml");

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
        return new ContentResult
        {
            StatusCode = (int)upstream.StatusCode,
            ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/json",
            Content = content
        };
    }
}

public sealed record GatewayLoginRequest(string UserName, string Password, string? Tenant = null);
