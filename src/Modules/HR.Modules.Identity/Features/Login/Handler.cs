using HR.Modules.Identity;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Identity.Features.Login;

internal sealed class LoginHandler(
    ISupabaseAuthGateway supabaseAuthGateway,
    IdentityDbContext dbContext,
    IServiceProvider serviceProvider,
    IAuthorizationService authorizationService,
    ILogger<LoginHandler> logger)
{
    public async Task<Result<LoginResponse>> HandleAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        SupabaseSession session;
        try
        {
            session = await supabaseAuthGateway.SignInWithPasswordAsync(
                request.Email.Trim(), request.Password, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            // Supabase's password-grant sign-in failing (wrong password, unknown email, or an
            // unconfirmed pending account) is a normal, expected outcome here, not a server
            // fault — SignInWithPasswordAsync throws for any non-success response, so this is the
            // only signal available to distinguish "bad credentials" from a genuine gateway bug.
            // The message sent back to the caller is deliberately the same regardless of which of
            // those it was, to avoid leaking which emails are registered — but the real Supabase
            // response (SignInWithPasswordAsync includes the raw response body in ex.Message) is
            // logged server-side, since "invalid email or password" alone doesn't distinguish a
            // genuine bad-credentials case from an unverified gateway assumption misfiring (see
            // the several UNVERIFIED comments elsewhere in SupabaseAuthGateway). SignInWithPasswordAsync
            // now redacts tokens/links from ex.Message, and the email is masked before logging.
            // No safe identifier is available yet at this point (sign-in has not resolved a local
            // UserProfile) — deliberately does not log the email address. ex.Message has already
            // been reduced by SupabaseAuthGateway to redact tokens/links before it reaches here.
            logger.LogWarning(ex, "Login failed at Supabase sign-in.");
            return Result.Failure<LoginResponse>(Error.Validation("Invalid email or password."));
        }

        var profile = await dbContext.UserProfiles
            .FirstOrDefaultAsync(p => p.SupabaseAuthUserId == session.UserId, cancellationToken);

        if (profile is null)
        {
            logger.LogWarning(
                "Login succeeded at Supabase but found no matching UserProfile (Supabase user id {SupabaseUserId})",
                session.UserId);
            return Result.Failure<LoginResponse>(Error.Validation("Invalid email or password."));
        }

        var isAllowed = await serviceProvider.TryDevSignInAsync(profile.Id);
        if (!isAllowed)
        {
            return Result.Failure<LoginResponse>(Error.Validation("Your account has been disabled."));
        }

        var roles = await authorizationService.GetEffectiveRolesAsync(profile.Id, cancellationToken);
        if (roles.Count == 0)
        {
            logger.LogWarning(
                "Login succeeded at Supabase and resolved UserProfile {UserProfileId}, but the account has no roles — rejecting as invalid",
                profile.Id);
            return Result.Failure<LoginResponse>(Error.Validation("Invalid email or password."));
        }

        var expiresInSeconds = (int)Math.Max(1, (session.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
        return Result.Success(new LoginResponse(session.AccessToken, session.RefreshToken, expiresInSeconds));
    }
}
