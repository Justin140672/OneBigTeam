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
// EnsureDevUserAsync/SignInWithPasswordAsync are different: E2E's persona-switching (the primary
// "log in as X" mechanism across most of the suite) goes through the same real Supabase
// password-grant path as local dev now, and — unlike invite/resend — plain sign-in doesn't send
// email, so it isn't subject to the same rate-limit concern. These two delegate to a real
// SupabaseAuthGateway instance instead of faking, which requires the same dev Supabase secrets as
// local dev (SupabaseAuth:ProjectUrl/PublishableKey/SecretKey/JwksUrl) to be available wherever
// E2E runs — already true via user-secrets for a local run; a CI E2E job would need them supplied
// as environment variables instead.
internal sealed class FakeSupabaseAuthGateway(IHttpClientFactory httpClientFactory, IOptions<SupabaseAuthOptions> options) : ISupabaseAuthGateway
{
    private readonly SupabaseAuthGateway _real = new(httpClientFactory, options);

    public Task<Guid> CreateUserAsync(string email, string redirectTo, CancellationToken cancellationToken) =>
        Task.FromResult(Guid.NewGuid());

    public Task ResendVerificationEmailAsync(string email, string redirectTo, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task RequestPasswordResetAsync(string email, string redirectTo, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task UpdatePasswordAsync(string userAccessToken, string newPassword, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<SupabaseSession> ExchangeCodeForSessionAsync(string code, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("No live Supabase project is configured for E2E testing.");

    public Task<Guid> EnsureDevUserAsync(string email, string password, CancellationToken cancellationToken) =>
        _real.EnsureDevUserAsync(email, password, cancellationToken);

    public Task<SupabaseSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken) =>
        _real.SignInWithPasswordAsync(email, password, cancellationToken);
}
