using Aneiang.Yarp.Extensions;
using Aneiang.Yarp.Dashboard.Extensions;
using Aneiang.Yarp.Dashboard.Infrastructure.Deployment;
using Aneiang.Yarp.Storage.Sqlite;
using Industrial.Gateway.Host.Health;
using Industrial.Gateway.Host.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;

var builder = WebApplication.CreateBuilder(args);
// MES 业务 API 的最终授权由 MOL 自己负责。额外配置仅覆盖 MES Route 的
// Gateway AuthorizationPolicy，避免 Local JWT 在 Gateway 处被 IAM JwtBearer 提前拒绝。
builder.Configuration.AddJsonFile("appsettings.MesPassThrough.json", optional: true, reloadOnChange: true);
// The dashboard serves Razor class-library assets under /_content/... .
// Explicitly enable the generated static-web-assets manifest so the dashboard
// endpoint can resolve those files in both Development and executable runs.
builder.WebHost.UseStaticWebAssets();
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
if (builder.Environment.IsDevelopment())
    builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
var iam = builder.Configuration.GetSection("Iam");

builder.Services.AddAneiangYarp(enableRegistration: false);
// S08 keeps the console self-contained: route/config metadata is persisted in
// the Gateway SQLite store while IAM remains reachable only through Gateway APIs.
builder.Services.AddAneiangStorage();
builder.Services.AddAneiangYarpDashboard();
builder.Services.AddAneiangYarpDeployment();
// The dashboard package contributes MVC controllers and Razor views. Register the
// full MVC-with-views stack in the host so its platform pages are discoverable.
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
        options.Authority = iam["Authority"] ?? "http://localhost:5100";
        options.Audience = iam["Audience"] ?? "industrial-platform";
        options.RequireHttpsMetadata = options.Authority.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    });
builder.Services.AddAuthorization(options =>
    options.AddPolicy("gateway", policy => policy.RequireAuthenticatedUser()));
builder.Services.AddHealthChecks();

var app = builder.Build();
// Establish X-Trace-Id before routing/proxy execution so YARP forwards the same
// identifier to every downstream service and returns it to the caller.
app.UseMiddleware<TraceContextMiddleware>();
app.UseRouting();
app.UseCors("GatewayFrontend");
app.UseAuthentication();
app.UseAneiangYarpDashboard(new DashboardApplicationBuilderExtensions.DashboardUseOptions
{
    // The dashboard extension owns the proxy endpoint and authorization middleware.
    AutoUseAuthorization = true
});
app.MapIndustrialHealth("industrial-gateway");
// Dashboard MVC pages and APIs are supplied by Aneiang.Yarp.Dashboard.
// They are mounted under Gateway:Dashboard:RoutePrefix ("platform" for S08).
app.MapControllers();
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/healthz");
app.MapGet("/", () => Results.Ok(new { service = "Industrial.Gateway", status = "ready" }));
app.Run();
