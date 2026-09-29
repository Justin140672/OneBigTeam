using System.Net;

namespace HR.Api.Startup;

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
		if (context.Request.Path.StartsWithSegments("/api/dev"))
		{
			var remoteIp = context.Connection.RemoteIpAddress;

			// Kestrel's dual-stack socket binding (the default on Linux, including CI runners)
			// reports an IPv4 loopback client as the IPv4-mapped IPv6 address ::ffff:127.0.0.1
			// rather than 127.0.0.1 itself — unmap it first so IsLoopback below actually
			// recognizes it, instead of rejecting every /api/dev/* request unconditionally on
			// platforms/environments where this mapping occurs.
			if (remoteIp is { IsIPv4MappedToIPv6: true })
			{
				remoteIp = remoteIp.MapToIPv4();
			}

			// A null RemoteIpAddress means there is no real TCP socket behind this request at
			// all — Kestrel over a genuine network connection always populates it. The only
			// common case where it's null is an in-process test host (WebApplicationFactory's
			// TestServer, used throughout HR.Integration.Tests), which has no network path and
			// is therefore inherently local. Only reject when we have an address and it's
			// genuinely not loopback — don't reject an absent one.
			if (remoteIp != null && !IPAddress.IsLoopback(remoteIp))
			{
				_logger.LogWarning(
					"Rejected /api/dev request from non-loopback address: {RemoteIp}",
					remoteIp.ToString());
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
