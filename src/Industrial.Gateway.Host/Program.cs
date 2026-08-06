using Aneiang.Yarp.Extensions;
using Aneiang.Yarp.Dashboard.Extensions;
using Aneiang.Yarp.Dashboard.Infrastructure.Deployment;
using Aneiang.Yarp.Storage.Sqlite;
using Industrial.Gateway.Host.Controllers;
using Industrial.Gateway.Host.Health;
using Industrial.Gateway.Host.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Migration is the safe default. In Migration mode legacy business JWTs are allowed
// to pass the Gateway and downstream services remain the final authorization point.
// Centralized mode stops loading the compatibility overrides so every route that is
// marked with AuthorizationPolicy=gateway in the base config requires an IAM JWT.
var cutoverMode = builder.Configuration["Gateway:Security:CutoverMode"] ?? "Migration";
var centralizedCutover = cutoverMode.Equals("Centralized", StringComparison.OrdinalIgnoreCase);
if (!centralizedCutover)
{
    builder.Configuration.AddJsonFile("appsettings.MesPassThrough.json", optional: true, reloadOnChange: true);
    builder.Configuration.AddJsonFile("appsettings.PlatformCompatibility.json", optional: true, reloadOnChange: true);
}

builder.WebHost.UseStaticWebAssets();
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
if (builder.Environment.IsDevelopment())
    builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();

var iam = builder.Configuration.GetSection("Iam");
var authority = iam["Authority"] ?? "http://localhost:5100";
var allowInsecureHttpAuthority = builder.Configuration.GetValue<bool>("Gateway:Security:AllowInsecureHttpAuthority");
if (!builder.Environment.IsDevelopment()
    && authority.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
    && !IsLoopbackAuthority(authority)
    && !allowInsecureHttpAuthority)
{
    throw new InvalidOperationException(
        "Production Gateway requires an HTTPS IAM authority. Set Gateway:Security:AllowInsecureHttpAuthority=true only for an explicitly accepted isolated-site exception.");
}

builder.Services.AddAneiangYarp(enableRegistration: false);
builder.Services.AddAneiangStorage();
builder.Services.AddAneiangYarpDashboard(options =>
{
    // The security-center login is the bootstrap surface. Every other Dashboard
    // request must carry a GatewayDashboard session that was established only after
    // IAM accepted the user's credentials.
    options.AuthorizeRequest = async context =>
    {
        if (context.Request.Path.StartsWithSegments("/platform/security"))
            return true;

        var session = await context.AuthenticateAsync(PlatformSecurityController.DashboardCookieScheme);
        if (!session.Succeeded || session.Principal is null)
            return false;

        context.User = session.Principal;
        return true;
    };
});
builder.Services.AddAneiangYarpDeployment();
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();
var frontendOrigins = builder.Configuration.GetSection("Gateway:Cors:AllowedOrigins").Get<string[]>()
    ?? new[]
    {
        "http://localhost:5173",
        "http://localhost:8080",
        "http://localhost:9990",
        "http://localhost:27915"
    };
builder.Services.AddCors(options => options.AddPolicy("GatewayFrontend", policy =>
    policy.WithOrigins(frontendOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = authority;
        options.Audience = iam["Audience"] ?? "industrial-platform";
        options.RequireHttpsMetadata = authority.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    })
    .AddCookie(PlatformSecurityController.DashboardCookieScheme, options =>
    {
        options.Cookie.Name = "industrial_gateway_dashboard";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(1);
        options.SlidingExpiration = false;
        options.LoginPath = "/platform/security";
        options.AccessDeniedPath = "/platform/security";
    });
builder.Services.AddAuthorization(options =>
    options.AddPolicy("gateway", policy => policy.RequireAuthenticatedUser()));
builder.Services.AddHealthChecks();

var app = builder.Build();
app.Logger.LogInformation("Gateway security cutover mode: {CutoverMode}", centralizedCutover ? "Centralized" : "Migration");

app.UseMiddleware<TraceContextMiddleware>();
app.UseRouting();
app.UseCors("GatewayFrontend");
app.UseAuthentication();
app.UseAneiangYarpDashboard(new DashboardApplicationBuilderExtensions.DashboardUseOptions
{
    AutoUseAuthorization = true
});
app.MapIndustrialHealth("industrial-gateway");
app.MapControllers();
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/healthz");
app.MapGet("/", () => Results.Ok(new
{
    service = "Industrial.Gateway",
    status = "ready",
    securityMode = centralizedCutover ? "Centralized" : "Migration"
}));
app.Run();

static bool IsLoopbackAuthority(string authority)
{
    if (!Uri.TryCreate(authority, UriKind.Absolute, out var uri)) return false;
    return uri.IsLoopback
        || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase);
}
