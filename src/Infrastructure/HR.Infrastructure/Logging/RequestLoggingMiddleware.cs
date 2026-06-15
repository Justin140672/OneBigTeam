using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace HR.Infrastructure.Logging;

/// <summary>
/// Logs request started, request completed, and request failed events.
/// Enriches completion logs with UserId, CompanyId, and EmployeeId extracted
/// from auth claims and route values (available after the inner pipeline runs).
/// Health check endpoints are excluded to avoid noise.
/// </summary>
public sealed class RequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestLoggingMiddleware> logger)
{
    private static readonly HashSet<string> SkippedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health", "/alive"
    };

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";

        if (SkippedPaths.Contains(path))
        {
            await next(context);
            return;
        }

        var method = context.Request.Method;
        var sw = Stopwatch.StartNew();

        logger.LogInformation("HTTP {Method} {Path} started", method, path);

        try
        {
            await next(context);
            sw.Stop();

            var statusCode = context.Response.StatusCode;
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var companyId = context.GetRouteValue("companyId")?.ToString();
            var employeeId = context.GetRouteValue("employeeId")?.ToString();

            using var userContext = PushUserContext(userId, companyId, employeeId);

            var level = statusCode >= 500 ? LogLevel.Error : LogLevel.Information;
            logger.Log(level,
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms",
                method, path, statusCode, sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (LogError(ex, method, path, sw.ElapsedMilliseconds))
        {
            // LogError always returns false — exception is re-thrown
        }
    }

    private bool LogError(Exception ex, string method, string path, long elapsedMs)
    {
        logger.LogError(ex, "HTTP {Method} {Path} failed after {ElapsedMs}ms", method, path, elapsedMs);
        return false;
    }

    private static IDisposable PushUserContext(string? userId, string? companyId, string? employeeId)
    {
        var stack = new List<IDisposable>(3);
        if (userId is not null) stack.Add(LogContext.PushProperty("UserId", userId));
        if (companyId is not null) stack.Add(LogContext.PushProperty("CompanyId", companyId));
        if (employeeId is not null) stack.Add(LogContext.PushProperty("EmployeeId", employeeId));
        return stack.Count == 0 ? NullDisposable.Instance : new CompositeDisposable(stack);
    }
}

file sealed class NullDisposable : IDisposable
{
    public static readonly NullDisposable Instance = new();
    public void Dispose() { }
}

file sealed class CompositeDisposable(IReadOnlyList<IDisposable> items) : IDisposable
{
    public void Dispose()
    {
        foreach (var item in items) item.Dispose();
    }
}
