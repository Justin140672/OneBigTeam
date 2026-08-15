using HR.Modules.Identity;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Identity.Features.Login;

// The real, environment-agnostic sign-in path — replaces HR.Web's earlier dev-persona-only login
// stub, which only ever matched a hardcoded set of seeded personas against a literal "password"
// and never called Supabase at all. Every dev persona already has a real Supabase account (see
// IdentityModule.SeedDevSupabaseUsersAsync, seeded with SupabaseAuthGateway.DevSupabasePassword),
// so this same real sign-in path also serves Development without a separate shortcut.
internal sealed class LoginHandler(
    ISupabaseAuthGateway supabaseAuthGateway,
    IdentityDbContext dbContext,
    IServiceProvider serviceProvider,
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
            // the several UNVERIFIED comments elsewhere in SupabaseAuthGateway).
            logger.LogWarning(ex, "Login failed at Supabase sign-in for {Email}", request.Email);
            return Result.Failure<LoginResponse>(Error.Validation("Invalid email or password."));
        }

        var profile = await dbContext.UserProfiles
            .FirstOrDefaultAsync(p => p.SupabaseAuthUserId == session.UserId, cancellationToken);

        if (profile is null)
        {
            // A confirmed Supabase account with no matching UserProfile row shouldn't happen for
            // any real signup path in this app, but fail closed rather than let an orphaned
            // Supabase identity through with no local profile for downstream code to resolve.
            logger.LogWarning(
                "Login succeeded at Supabase but found no matching UserProfile for {Email} (Supabase user id {SupabaseUserId})",
                request.Email, session.UserId);
            return Result.Failure<LoginResponse>(Error.Validation("Invalid email or password."));
        }

        // Reuses the same IsActive gate + LastLoginAt recording as the dev persona switcher
        // (IdentityModule.TryDevSignInAsync) — despite the "Dev" name, that method's own logic is
        // environment-agnostic (a plain active-check + login timestamp), only its caller
        // (HR.Api's /api/dev/persona/{userId} route) is Development-gated.
        var isAllowed = await serviceProvider.TryDevSignInAsync(profile.Id);
        if (!isAllowed)
        {
            return Result.Failure<LoginResponse>(Error.Validation("Your account has been disabled."));
        }

        var expiresInSeconds = (int)Math.Max(1, (session.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
        return Result.Success(new LoginResponse(session.AccessToken, session.RefreshToken, expiresInSeconds));
    }
}
