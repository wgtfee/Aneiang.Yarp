using Aneiang.Yarp.Extensions;
using Aneiang.Yarp.Dashboard.Extensions;
using Aneiang.Yarp.Dashboard.Infrastructure.Deployment;
using Aneiang.Yarp.Storage.Sqlite;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);
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
app.UseRouting();
app.UseAuthentication();
app.UseAneiangYarpDashboard(new DashboardApplicationBuilderExtensions.DashboardUseOptions
{
    // The dashboard extension owns the proxy endpoint and authorization middleware.
    AutoUseAuthorization = true
});
// Dashboard MVC pages and APIs are supplied by Aneiang.Yarp.Dashboard.
// They are mounted under Gateway:Dashboard:RoutePrefix ("platform" for S08).
app.MapControllers();
app.MapHealthChecks("/health/ready");
app.MapGet("/", () => Results.Ok(new { service = "Industrial.Gateway", status = "ready" }));
app.Run();
