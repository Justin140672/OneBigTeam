namespace HR.Modules.Identity.Services;

// Registered instead of SupabaseAuthGateway only when E2E_TESTING=true (see IdentityModule.cs).
// The E2E suite's signup/resend journeys (SignupToCheckYourEmailJourneyTests) create a real
// Supabase Auth user per run and were hitting Supabase's rate limits on repeated CI/local runs —
// this avoids any live HTTP call for those flows. VerifyEmailJourneyTests deliberately still
// exercises the real failure path (no live Supabase project's verification codes are reachable
// from E2E either way), so ExchangeCodeForSessionAsync failing here matches that existing,
// documented "no live Supabase project" behaviour rather than starting to fake success.
internal sealed class FakeSupabaseAuthGateway : ISupabaseAuthGateway
{
    public Task<Guid> CreateUserAsync(string email, string redirectTo, CancellationToken cancellationToken) =>
        Task.FromResult(Guid.NewGuid());

    public Task ResendVerificationEmailAsync(string email, string redirectTo, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<SupabaseSession> ExchangeCodeForSessionAsync(string code, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("No live Supabase project is configured for E2E testing.");

    // E2E's persona-switching also goes through the real password-grant path per the "Switch
    // development to real Supabase auth" plan, which requires the same dev Supabase secrets as
    // local dev to be configured for CI (flagged as a follow-up in that plan, not solved here).
    // These fakes exist only so the admin-API surface used by signup/resend keeps working without
    // a live Supabase project for the rest of the E2E suite.
    // Deterministic per-email id (rather than Guid.NewGuid()) so repeated calls for the same email
    // within a run — e.g. IdentityModule.SeedDevSupabaseUsersAsync re-seeding on every startup —
    // return a stable id, matching SupabaseAuthGateway's real idempotent lookup-by-email behaviour.
    public Task<Guid> EnsureDevUserAsync(string email, string password, CancellationToken cancellationToken) =>
        Task.FromResult(DeterministicGuid(email));

    private static Guid DeterministicGuid(string input)
    {
        var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return new Guid(hash);
    }

    public Task<SupabaseSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("No live Supabase project is configured for E2E testing.");
}
