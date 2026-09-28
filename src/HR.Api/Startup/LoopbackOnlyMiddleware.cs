using System.Net;

namespace HR.Api.Startup;

/// <summary>
/// Middleware that restricts access to development endpoints to loopback addresses only.
///
/// After any trusted proxy handling, this verifies the request originates from a genuinely
/// local address (127.0.0.1, ::1, or similar). Rejects non-loopback requests with 403 Forbidden.
///
/// This is a defense-in-depth layer for /api/dev/* endpoints when DevTools is enabled:
/// even if DevTools is accidentally exposed in a development-like environment accessible
/// over a network, the endpoints remain unreachable except from localhost.
/// </summary>
public class LoopbackOnlyMiddleware
{
	private readonly RequestDelegate _next;
	private readonly ILogger<LoopbackOnlyMiddleware> _logger;

	public LoopbackOnlyMiddleware(RequestDelegate next, ILogger<LoopbackOnlyMiddleware> logger)
	{
		_next = next;
		_logger = logger;
	}

	public async Task InvokeAsync(HttpContext context)
	{
		// Check if this is a /api/dev/* request and if so, verify it's from loopback.
		if (context.Request.Path.StartsWithSegments("/api/dev"))
		{
			var remoteIp = context.Connection.RemoteIpAddress;

			// After trusted proxy middleware has run, RemoteIpAddress should reflect the
			// real caller's IP. Check if it's a loopback address (127.0.0.1, ::1, etc.).
			if (remoteIp == null || !IPAddress.IsLoopback(remoteIp))
			{
				_logger.LogWarning(
					"Rejected /api/dev request from non-loopback address: {RemoteIp}",
					remoteIp?.ToString() ?? "unknown");
				context.Response.StatusCode = StatusCodes.Status403Forbidden;
				return;
			}
		}

		await _next(context);
	}
}

public static class LoopbackOnlyMiddlewareExtensions
{
	public static IApplicationBuilder UseLoopbackOnlyForDevTools(this IApplicationBuilder builder)
	{
		return builder.UseMiddleware<LoopbackOnlyMiddleware>();
	}
}
