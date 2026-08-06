using System.Diagnostics;

namespace Industrial.Gateway.Host.Infrastructure;

/// <summary>
/// Establishes a stable trace identifier at the platform edge. The request header is
/// updated before YARP runs so the same value is forwarded to downstream services.
/// </summary>
public sealed class TraceContextMiddleware(RequestDelegate next, ILogger<TraceContextMiddleware> logger)
{
    public const string HeaderName = "X-Trace-Id";
    private const int MaxTraceIdLength = 128;

    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = TryReadIncomingTraceId(context)
            ?? Activity.Current?.TraceId.ToString()
            ?? Guid.NewGuid().ToString("N");

        context.TraceIdentifier = traceId;
        context.Request.Headers[HeaderName] = traceId;
        Activity.Current?.SetTag("industrial.trace_id", traceId);

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = traceId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["TraceId"] = traceId }))
            await next(context);
    }

    private static string? TryReadIncomingTraceId(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var values)) return null;
        var value = values.ToString().Trim();
        if (value.Length is 0 or > MaxTraceIdLength) return null;

        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_' or '.') continue;
            return null;
        }

        return value;
    }
}
