using HR.Modules.Identity.Services;

namespace HR.Integration.Tests.Infrastructure;

/// <summary>
/// Test double for the Identity module's internal ISupabaseAuthGateway, registered against the
/// shared integration test host (see ApiWebApplicationFactory). Never calls the real Supabase Auth
/// API. Safe to mutate per-test because the assembly disables test parallelization (see
/// AssemblyInfo.cs), so tests run strictly sequentially against the shared factory.
/// </summary>
internal sealed class FakeSupabaseAuthGateway : ISupabaseAuthGateway
{
    public List<(string Email, string RedirectTo)> CreatedUsers { get; } = [];
    public List<(string Email, string RedirectTo)> ResentEmails { get; } = [];
    public List<(string Email, string RedirectTo)> PasswordResetRequests { get; } = [];
    public List<(string Email, string RedirectTo)> RecoveryLinksGenerated { get; } = [];
    public List<(string AccessToken, string NewPassword)> PasswordUpdates { get; } = [];

    public string RecoveryLinkToReturn { get; set; } = "https://example.supabase.co/auth/v1/verify?token=fake-recovery&type=recovery";

    public Guid? UserIdToReturn { get; set; }
    public bool ShouldThrowOnCreate { get; set; }
    public bool ShouldThrowOnExchange { get; set; }
    public bool ShouldThrowOnSignIn { get; set; }

    public List<(string Email, string Password)> EnsuredDevUsers { get; } = [];
    public List<(string Email, string Password)> SignedInUsers { get; } = [];
    public List<(string Email, string Password)> ConfirmedUsersCreated { get; } = [];

    public Task<Guid> CreateUserAsync(string email, string password, string redirectTo, CancellationToken cancellationToken)
    {
        if (ShouldThrowOnCreate)
        {
            throw new InvalidOperationException("Simulated Supabase failure.");
        }

        CreatedUsers.Add((email, redirectTo));
        return Task.FromResult(UserIdToReturn ?? Guid.NewGuid());
    }

    public Task ResendVerificationEmailAsync(string email, string redirectTo, CancellationToken cancellationToken)
    {
        ResentEmails.Add((email, redirectTo));
        return Task.CompletedTask;
    }

    public Task RequestPasswordResetAsync(string email, string redirectTo, CancellationToken cancellationToken)
    {
        PasswordResetRequests.Add((email, redirectTo));
        return Task.CompletedTask;
    }

    public Task<string> GenerateRecoveryLinkAsync(string email, string redirectTo, CancellationToken cancellationToken)
    {
        RecoveryLinksGenerated.Add((email, redirectTo));
        return Task.FromResult(RecoveryLinkToReturn);
    }

    public Task UpdatePasswordAsync(string userAccessToken, string newPassword, CancellationToken cancellationToken)
    {
        PasswordUpdates.Add((userAccessToken, newPassword));
        return Task.CompletedTask;
    }

    public Task<SupabaseSession> ExchangeCodeForSessionAsync(string code, CancellationToken cancellationToken)
    {
        if (ShouldThrowOnExchange)
        {
            throw new InvalidOperationException("Simulated invalid/expired Supabase verification code.");
        }

        // Reuses UserIdToReturn (the same knob SignUp tests set before calling /api/signup) so a
        // test can drive the full SignUp -> VerifyEmail flow against the same Supabase auth user
        // id without a separate code->session mapping.
        return Task.FromResult(new SupabaseSession("access-token", "refresh-token", UserIdToReturn ?? Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1)));
    }

    public Task<Guid> EnsureDevUserAsync(string email, string password, CancellationToken cancellationToken)
    {
        EnsuredDevUsers.Add((email, password));
        return Task.FromResult(UserIdToReturn ?? Guid.NewGuid());
    }

    public Task<Guid> CreateConfirmedUserAsync(string email, string password, CancellationToken cancellationToken)
    {
        if (ShouldThrowOnCreate)
        {
            throw new InvalidOperationException("Simulated Supabase failure.");
        }

        ConfirmedUsersCreated.Add((email, password));
        return Task.FromResult(UserIdToReturn ?? Guid.NewGuid());
    }

    public Task<SupabaseSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken)
    {
        if (ShouldThrowOnSignIn)
        {
            throw new InvalidOperationException("Simulated Supabase sign-in failure.");
        }

        SignedInUsers.Add((email, password));
        return Task.FromResult(new SupabaseSession("access-token", "refresh-token", UserIdToReturn ?? Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1)));
    }

    public void Reset()
    {
        CreatedUsers.Clear();
        ResentEmails.Clear();
        PasswordResetRequests.Clear();
        RecoveryLinksGenerated.Clear();
        PasswordUpdates.Clear();
        UserIdToReturn = null;
        ShouldThrowOnCreate = false;
        ShouldThrowOnExchange = false;
        ShouldThrowOnSignIn = false;
        EnsuredDevUsers.Clear();
        SignedInUsers.Clear();
        ConfirmedUsersCreated.Clear();
    }
}
