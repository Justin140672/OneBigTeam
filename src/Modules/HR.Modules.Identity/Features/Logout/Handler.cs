using HR.Modules.Identity.Services;
using HR.SharedKernel;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Identity.Features.Logout;

// Best-effort server-side revocation of the caller's Supabase session on sign-out. The caller
// (HR.Web's /logout) presents the access token from its session cookie as a bearer; that token
// authenticates the request to Supabase's GoTrue logout endpoint, not to us, so this endpoint is
// anonymous. A failure here (token already expired, GoTrue unavailable) must NOT block sign-out —
// HR.Web clears its cookie regardless — so this always reports success and only logs a warning,
// never the token.
internal sealed class LogoutHandler(
    ISupabaseAuthGateway supabaseAuthGateway,
    ILogger<LogoutHandler> logger)
{
    public async Task<Result<LogoutResponse>> HandleAsync(string? accessToken, CancellationToken cancellationToken)
    {
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
