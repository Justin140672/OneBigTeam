using HR.Modules.Identity.Services;
using HR.SharedKernel;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Identity.Features.Logout;

// Server-side revocation of the caller's Supabase session on sign-out (Ticket 13 hardens this
// beyond the prior best-effort-only Supabase call). The caller (HR.Web's/HR.Admin.Web's /logout)
// presents the access token from its session cookie as a bearer; that token authenticates the
// request to Supabase's GoTrue logout endpoint, not to us, so this endpoint is anonymous. A failure
// calling Supabase (token already expired, GoTrue unavailable) must NOT block sign-out — HR.Web
// clears its cookie regardless — so this always reports success and only logs a warning, never the
// token.
//
// The local revocation write is the actual security boundary and is independent of Supabase's own
// (delayed, best-effort, possibly-failing) sign-out call: it is written FIRST, synchronously, using
// the "sub" claim already validated by HR.Api's own JWT bearer pipeline for this very request (see
// Endpoint — ASP.NET Core's authentication middleware always runs and populates HttpContext.User
// even for an [AllowAnonymous] endpoint; only the [Authorize] failure is skipped). Every other
// circuit/tab/replica's next authenticated request is checked against this record in
// SupabaseJwtBearerConfiguration's OnTokenValidated handler (see IdentityModule.IsSessionRevokedAsync),
// so logout takes effect immediately everywhere, even if the Supabase call below never completes.
internal sealed class LogoutHandler(
    ISupabaseAuthGateway supabaseAuthGateway,
    ISessionRevocationStore sessionRevocationStore,
    IClock clock,
    ILogger<LogoutHandler> logger)
{
    public async Task<Result<LogoutResponse>> HandleAsync(
        string? accessToken, Guid? supabaseAuthUserId, CancellationToken cancellationToken)
    {
        // Written before anything else in this handler, and awaited before the response is sent —
        // see the class remarks for why this (not the Supabase call below) is the real security
        // boundary. A missing sub claim (e.g. the presented bearer failed HR.Api's own signature/
        // issuer/audience validation) means there is no reliably-identified session to revoke; the
        // caller's cookie is still cleared regardless (HR.Web/HR.Admin.Web do that unconditionally).
        if (supabaseAuthUserId is { } userId)
        {
            await sessionRevocationStore.RevokeAsync(
                userId, new DateTimeOffset(clock.UtcNow, TimeSpan.Zero), cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Result.Success(new LogoutResponse(false));
        }

        try
        {
            await supabaseAuthGateway.SignOutAsync(accessToken, cancellationToken);
            return Result.Success(new LogoutResponse(true));
        }
        catch (InvalidOperationException ex)
        {
            // SignOutAsync redacts tokens/links from its message; log without any token value.
            logger.LogWarning(ex, "Supabase server-side sign-out failed; the session cookie is still cleared by the caller.");
            return Result.Success(new LogoutResponse(false));
        }
    }
}
