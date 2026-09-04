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

    // Simulates Supabase rejecting a recovery access token (expired / already used / tampered):
    // UpdatePasswordAsync surfaces any non-success Supabase response as an InvalidOperationException,
    // which ResetPasswordHandler maps to a generic validation failure rather than a 500.
    public bool ShouldThrowOnUpdatePassword { get; set; }

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
        if (ShouldThrowOnUpdatePassword)
        {
            throw new InvalidOperationException(
                $"Supabase /auth/v1/user request failed with status 401 (Unauthorized). Response body: {{\"error\":\"invalid token: {userAccessToken}\"}}");
        }

        PasswordUpdates.Add((userAccessToken, newPassword));
        return Task.CompletedTask;
    }

    public List<string> SignOutCalls { get; } = [];

    // Simulates GoTrue rejecting the sign-out (e.g. token already expired). The /logout journey must
    // still complete: the caller swallows this and clears the cookie regardless.
    public bool ShouldThrowOnSignOut { get; set; }

    public Task SignOutAsync(string userAccessToken, CancellationToken cancellationToken)
    {
        if (ShouldThrowOnSignOut)
        {
            throw new InvalidOperationException(
                "Supabase sign-out request failed with status 401 (Unauthorized). response body: (redacted)");
        }

        SignOutCalls.Add(userAccessToken);
        return Task.CompletedTask;
    }

    public List<Guid> MfaFactorRemovals { get; } = [];
    public bool ShouldThrowOnRemoveMfaFactors { get; set; }
    public int MfaFactorsRemovedToReturn { get; set; } = 2;

    public Task<int> RemoveAllMfaFactorsAsync(Guid supabaseUserId, CancellationToken cancellationToken)
    {
        if (ShouldThrowOnRemoveMfaFactors)
        {
            throw new InvalidOperationException(
                "Supabase delete-MFA-factor request failed with status 500 (InternalServerError). Response body: {\"error\":\"simulated\"}");
        }

        MfaFactorRemovals.Add(supabaseUserId);
        return Task.FromResult(MfaFactorsRemovedToReturn);
    }

    /// <summary>Populate to have <see cref="GetUserIdByEmailAsync"/> resolve an id for that email.</summary>
    public Dictionary<string, Guid> UserIdsByEmail { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<Guid?> GetUserIdByEmailAsync(string email, CancellationToken cancellationToken) =>
        Task.FromResult(UserIdsByEmail.TryGetValue(email.Trim(), out var id) ? id : (Guid?)null);

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
        SignOutCalls.Clear();
        ShouldThrowOnSignOut = false;
        MfaFactorRemovals.Clear();
        ShouldThrowOnRemoveMfaFactors = false;
        MfaFactorsRemovedToReturn = 2;
        UserIdToReturn = null;
        ShouldThrowOnCreate = false;
        ShouldThrowOnExchange = false;
        ShouldThrowOnSignIn = false;
        ShouldThrowOnUpdatePassword = false;
        EnsuredDevUsers.Clear();
        SignedInUsers.Clear();
        ConfirmedUsersCreated.Clear();
    }
}
