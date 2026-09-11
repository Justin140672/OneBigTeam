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

        // ASP.NET Core's authentication middleware always runs (UseAuthentication), even for an
        // [AllowAnonymous] endpoint — only the later [Authorize] failure is skipped. So when the
        // presented bearer is a genuine, currently-valid Supabase access token, HR.Api's own JWT
        // bearer pipeline has already validated its signature/issuer/audience/lifetime and populated
        // HttpContext.User by the time this runs. Reading "sub" from there (rather than decoding the
        // raw header ourselves) means the user id trusted for revocation is never taken from an
        // unverified, caller-supplied token.
        Guid? supabaseAuthUserId = null;
        if (HttpContext.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(HttpContext.User.FindFirst("sub")?.Value, out var parsedUserId))
        {
            supabaseAuthUserId = parsedUserId;
        }

        var result = await handler.HandleAsync(accessToken, supabaseAuthUserId, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
