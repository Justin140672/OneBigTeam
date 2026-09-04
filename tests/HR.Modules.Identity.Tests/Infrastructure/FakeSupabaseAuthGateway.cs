using HR.Modules.Identity.Services;

namespace HR.Modules.Identity.Tests.Infrastructure;

internal sealed class FakeSupabaseAuthGateway : ISupabaseAuthGateway
{
    public List<(string Email, string RedirectTo)> CreatedUsers { get; } = [];
    public List<(string Email, string Password, string RedirectTo)> CreatedUsersWithPassword { get; } = [];
    public List<(string Email, string RedirectTo)> ResentEmails { get; } = [];
    public List<(string Email, string RedirectTo)> PasswordResetRequests { get; } = [];
    public List<(string Email, string RedirectTo)> RecoveryLinksGenerated { get; } = [];

    public string RecoveryLinkToReturn { get; set; } = "https://example.supabase.co/auth/v1/verify?token=fake-recovery&type=recovery";

    public Guid? UserIdToReturn { get; set; }
    public bool ShouldThrowOnCreate { get; set; }
    public bool ShouldThrowEmailAlreadyRegistered { get; set; }
    public bool ShouldThrowOnExchange { get; set; }
    public bool ShouldThrowOnSignIn { get; set; }

    public List<(string Email, string Password)> EnsuredDevUsers { get; } = [];
    public List<(string Email, string Password)> SignedInUsers { get; } = [];
    public List<(string Email, string Password)> ConfirmedUsersCreated { get; } = [];

    public Task<Guid> CreateUserAsync(string email, string password, string redirectTo, CancellationToken cancellationToken)
    {
        if (ShouldThrowEmailAlreadyRegistered)
        {
            throw new EmailAlreadyRegisteredException(email);
        }

        if (ShouldThrowOnCreate)
        {
            throw new InvalidOperationException("Simulated Supabase failure.");
        }

        CreatedUsers.Add((email, redirectTo));
        CreatedUsersWithPassword.Add((email, password, redirectTo));
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

    public bool ShouldThrowOnUpdatePassword { get; set; }

    /// <summary>
    /// Simulates the raw Supabase /auth/v1/user error message a real gateway failure would carry —
    /// deliberately token-shaped so tests can prove it never reaches a log or an API response.
    /// </summary>
    public string UpdatePasswordErrorMessage { get; set; } =
        "Supabase update-password request failed with status 401 (Unauthorized). " +
        "response detail: access_token=eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.abc123signature";

    public Task UpdatePasswordAsync(string userAccessToken, string newPassword, CancellationToken cancellationToken)
    {
        if (ShouldThrowOnUpdatePassword)
            throw new InvalidOperationException(UpdatePasswordErrorMessage);

        return Task.CompletedTask;
    }

    public List<string> SignOutCalls { get; } = [];
    public bool ShouldThrowOnSignOut { get; set; }

    public Task SignOutAsync(string userAccessToken, CancellationToken cancellationToken)
    {
        if (ShouldThrowOnSignOut)
            throw new InvalidOperationException(
                "Supabase sign-out request failed with status 500 (InternalServerError). response body: (redacted)");

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

    public Dictionary<string, Guid> UserIdsByEmail { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<Guid?> GetUserIdByEmailAsync(string email, CancellationToken cancellationToken) =>
        Task.FromResult(UserIdsByEmail.TryGetValue(email.Trim(), out var id) ? id : (Guid?)null);

    public Task<SupabaseSession> ExchangeCodeForSessionAsync(string code, CancellationToken cancellationToken)
    {
        if (ShouldThrowOnExchange)
        {
            throw new InvalidOperationException("Simulated invalid/expired Supabase verification code.");
        }

        return Task.FromResult(new SupabaseSession("access-token", "refresh-token", UserIdToReturn ?? Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1)));
    }

    public Task<Guid> EnsureDevUserAsync(string email, string password, CancellationToken cancellationToken)
    {
        EnsuredDevUsers.Add((email, password));
        return Task.FromResult(UserIdToReturn ?? Guid.NewGuid());
    }

    public Task<Guid> CreateConfirmedUserAsync(string email, string password, CancellationToken cancellationToken)
    {
        if (ShouldThrowEmailAlreadyRegistered)
        {
            throw new EmailAlreadyRegisteredException(email);
        }

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
}
