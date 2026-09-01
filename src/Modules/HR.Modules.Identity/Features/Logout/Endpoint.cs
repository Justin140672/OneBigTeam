using System.Net.Http.Headers;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace HR.Modules.Identity.Features.Logout;

// POST /api/logout — called by HR.Web's /logout path (never the browser directly) with the access
// token from its session cookie as "Authorization: Bearer ...". Anonymous: the bearer authenticates
// the caller to Supabase's GoTrue logout endpoint, not to this API, and sign-out must still work
// when that token is close to expiry. Always returns 200 so a revocation failure never blocks the
// user's sign-out (HR.Web clears its cookie regardless).
internal sealed class Endpoint(
    LogoutHandler handler) : EndpointWithoutRequest<LogoutResponse>
{
    public override void Configure()
    {
        Post("/api/logout");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        string? accessToken = null;
        if (HttpContext.Request.Headers.TryGetValue(HeaderNames.Authorization, out var header)
            && AuthenticationHeaderValue.TryParse(header.ToString(), out var parsed)
            && string.Equals(parsed.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            accessToken = parsed.Parameter;
        }

        var result = await handler.HandleAsync(accessToken, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
