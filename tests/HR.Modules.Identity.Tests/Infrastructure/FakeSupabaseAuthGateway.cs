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

    public int CreateUserCallCount { get; private set; }

    public List<Guid> DeletedUserIds { get; } = [];

    public bool ShouldThrowOnDelete { get; set; }

    public Func<string, Exception?>? FailAfterCreate { get; set; }

    public Task<Guid> CreateUserAsync(
        string email, string password, string redirectTo, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        CreateUserCallCount++;

        if (ShouldThrowEmailAlreadyRegistered || UserIdsByEmail.ContainsKey(email.Trim()))
        {
            throw new EmailAlreadyRegisteredException(email);
        }

        if (ShouldThrowOnCreate)
        {
            throw new InvalidOperationException("Simulated Supabase failure.");
        }

        CreatedUsers.Add((email, redirectTo));
        CreatedUsersWithPassword.Add((email, password, redirectTo));

        var userId = UserIdToReturn ?? Guid.NewGuid();
        UserIdsByEmail[email.Trim()] = userId;
        if (metadata is { Count: > 0 })
            MetadataByEmail[email.Trim()] = metadata;

        if (FailAfterCreate?.Invoke(email) is { } failure)
            throw failure;

        return Task.FromResult(userId);
    }

    public Task DeleteUserAsync(Guid supabaseUserId, CancellationToken cancellationToken)
    {
        if (ShouldThrowOnDelete)
            throw new InvalidOperationException("Simulated Supabase delete failure.");

        DeletedUserIds.Add(supabaseUserId);
        foreach (var email in UserIdsByEmail.Where(kv => kv.Value == supabaseUserId).Select(kv => kv.Key).ToList())
        {
            UserIdsByEmail.Remove(email);
            MetadataByEmail.Remove(email);
        }

        return Task.CompletedTask;
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

    public bool ShouldThrowOnGetUserIdByEmail { get; set; }

    public Func<string, Exception>? GetUserIdByEmailFailure { get; set; }

    public Task<Guid?> GetUserIdByEmailAsync(string email, CancellationToken cancellationToken)
    {
        if (GetUserIdByEmailFailure is not null)
            throw GetUserIdByEmailFailure(email);

        if (ShouldThrowOnGetUserIdByEmail)
            throw new InvalidOperationException("Simulated Supabase lookup failure.");

        return Task.FromResult(UserIdsByEmail.TryGetValue(email.Trim(), out var id) ? id : (Guid?)null);
    }

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

    public List<(string Email, string Password, IReadOnlyDictionary<string, string>? Metadata)> ConfirmedUsersCreatedWithMetadata { get; } = [];

    /// <summary>Ticket 12 (P1): metadata that GetUserMetadataByEmailAsync should report for a given
    /// email — tests set this directly to simulate a pre-existing account (with or without a
    /// matching provisioning correlation value) rather than relying only on whatever
    /// CreateConfirmedUserAsync itself recorded.</summary>
    public Dictionary<string, IReadOnlyDictionary<string, string>> MetadataByEmail { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<Guid> CreateConfirmedUserAsync(
        string email, string password, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? metadata = null)
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
        ConfirmedUsersCreatedWithMetadata.Add((email, password, metadata));

        if (metadata is { Count: > 0 })
            MetadataByEmail[email.Trim()] = metadata;

        return Task.FromResult(UserIdToReturn ?? Guid.NewGuid());
    }

    public Task<(Guid UserId, IReadOnlyDictionary<string, string> Metadata)?> GetUserMetadataByEmailAsync(
        string email, CancellationToken cancellationToken)
    {
        if (!UserIdsByEmail.TryGetValue(email.Trim(), out var userId))
            return Task.FromResult<(Guid, IReadOnlyDictionary<string, string>)?>(null);

        var metadata = MetadataByEmail.TryGetValue(email.Trim(), out var m)
            ? m
            : new Dictionary<string, string>();

        return Task.FromResult<(Guid, IReadOnlyDictionary<string, string>)?>((userId, metadata));
    }

    public List<(string Email, string RedirectTo, IReadOnlyDictionary<string, string> Metadata)> PendingUsersCreatedWithMetadata { get; } = [];

    public Func<string, Exception>? CreatePendingUserFailure { get; set; }

    public Task<Guid> CreatePendingUserWithMetadataAsync(
        string email, string redirectTo, IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken)
    {
        if (CreatePendingUserFailure is not null)
            throw CreatePendingUserFailure(email);

        if (ShouldThrowEmailAlreadyRegistered)
            throw new EmailAlreadyRegisteredException(email);

        if (ShouldThrowOnCreate)
            throw new InvalidOperationException("Simulated Supabase failure.");

        PendingUsersCreatedWithMetadata.Add((email, redirectTo, metadata));
        MetadataByEmail[email.Trim()] = metadata;

        var userId = UserIdToReturn ?? Guid.NewGuid();
        UserIdsByEmail[email.Trim()] = userId;
        return Task.FromResult(userId);
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
