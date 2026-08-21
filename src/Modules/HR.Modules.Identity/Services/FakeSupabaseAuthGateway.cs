using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace HR.Modules.Identity.Services;

// Registered instead of SupabaseAuthGateway only when E2E_TESTING=true (see IdentityModule.cs).
// Hybrid, not a pure fake: the E2E suite's signup/resend journeys
// (SignupToCheckYourEmailJourneyTests) create a real Supabase Auth user per run and send real
// emails — faked here to avoid hitting Supabase's email-sending rate limits on repeated CI/local
// runs. VerifyEmailJourneyTests deliberately still exercises the real failure path (no live
// Supabase project's verification codes are reachable from E2E either way), so
// ExchangeCodeForSessionAsync failing here matches that existing, documented behaviour rather than
// starting to fake success.
//
// EnsureDevUserAsync/SignInWithPasswordAsync used to delegate to a real SupabaseAuthGateway
// instance instead of faking, on the assumption that plain sign-in (unlike invite/resend, which
// sends email) wasn't subject to the same rate-limit concern. That assumption turned out to be
// wrong: live retry evidence (5 attempts, growing backoff up to 25s between each) still failed
// 3-in-a-row for multiple personas in the same run — Supabase's Auth API rate-limits sign-in too
// under this suite's login volume, and no amount of local retry/backoff/concurrency tuning can work
// around a quota that's exhausted on Supabase's side. These two are now faked as well, minting a
// locally-signed token via E2eFakeSupabaseJwt instead of calling Supabase at all — HR.Api's JWT
// bearer validation (Program.cs's ConfigureSupabaseJwtBearer) accepts these tokens ONLY when
// E2E_TESTING=true is also set for the API process itself (same flag, already proven to propagate
// there — it's what activates this very class), via an added signing-key candidate that's
// completely absent from the real Supabase JWKS validation path used everywhere else.
internal sealed class FakeSupabaseAuthGateway(IHttpClientFactory httpClientFactory, IOptions<SupabaseAuthOptions> options) : ISupabaseAuthGateway
{
    private readonly SupabaseAuthGateway _real = new(httpClientFactory, options);

    // Must derive the SAME deterministic id SignInWithPasswordAsync will later compute for this
    // email (see DeriveFakeUserId below) — self-service SignUp persists this returned id as
    // UserProfile.SupabaseAuthUserId, and LoginHandler looks up the UserProfile by the id
    // SignInWithPasswordAsync returns after a real form login. Previously returned Guid.NewGuid(),
    // an unrelated random id that could never match the deterministic one sign-in computes, so
    // every self-service-signed-up E2E account failed login with "Invalid email or password" even
    // though Supabase-side auth itself "succeeded" (see LoginHandler's UserProfile-not-found path).
    public Task<Guid> CreateUserAsync(string email, string password, string redirectTo, CancellationToken cancellationToken) =>
        Task.FromResult(DeriveFakeUserId(email));

    public Task ResendVerificationEmailAsync(string email, string redirectTo, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task RequestPasswordResetAsync(string email, string redirectTo, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task UpdatePasswordAsync(string userAccessToken, string newPassword, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<SupabaseSession> ExchangeCodeForSessionAsync(string code, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("No live Supabase project is configured for E2E testing.");

    public Task<Guid> EnsureDevUserAsync(string email, string password, CancellationToken cancellationToken) =>
        Task.FromResult(DeriveFakeUserId(email));

    public Task<Guid> CreateConfirmedUserAsync(string email, string password, CancellationToken cancellationToken) =>
        Task.FromResult(DeriveFakeUserId(email));

    public Task<SupabaseSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken)
    {
        // Every dev-persona/E2E login uses the same fixed seeded password (see
        // SupabaseAuthGateway.DevSupabasePassword and LoginPage.DevPersonaPassword) — checked here
        // so a deliberately-wrong-password E2E test still gets a rejection instead of unconditional
        // success, mirroring what real Supabase's password-grant endpoint would do.
        if (password != SupabaseAuthGateway.DevSupabasePassword)
        {
            throw new InvalidOperationException(
                $"Fake E2E Supabase sign-in rejected for '{email}': password did not match the seeded dev password.");
        }

        var userId = DeriveFakeUserId(email);
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        var accessToken = E2eFakeSupabaseJwt.CreateAccessToken(
            options.Value.ProjectUrl, userId, email, expiresAt - DateTimeOffset.UtcNow);

        return Task.FromResult(new SupabaseSession(accessToken, "e2e-fake-refresh-token", userId, expiresAt));
    }

    // Deterministic per-email GUID so repeated calls (EnsureDevUserAsync then SignInWithPasswordAsync,
    // or multiple SignInWithPasswordAsync calls across a run) always resolve to the same fake
    // Supabase user id for a given email — mirrors real Supabase's idempotent "already exists"
    // behaviour (SupabaseAuthGateway.EnsureDevUserAsync) without needing any storage.
    private static Guid DeriveFakeUserId(string email)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant()));
        return new Guid(hash);
    }
}
