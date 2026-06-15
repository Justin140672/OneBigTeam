using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace HR.Infrastructure.Logging;

/// <summary>
/// Extracts X-Correlation-ID from the incoming request (or generates one) and:
/// - stores it in HttpContext.Items for downstream middleware
/// - echoes it back in the response header
/// - pushes it into Serilog's LogContext so every log within the request includes it
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemsKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault()
            ?? Guid.NewGuid().ToString("D");

        context.Items[ItemsKey] = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
