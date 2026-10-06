using HR.Modules.Identity.Services;

namespace HR.Integration.Tests.Infrastructure;

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

    /// <summary>
    /// Ticket 8 (P2): when set, CreateConfirmedUserAsync throws EmailAlreadyRegisteredException for
    /// this exact email instead of creating a user — simulating Supabase already having the account
    /// from a previous (possibly interrupted) AcceptInvite attempt. Combine with UserIdsByEmail to
    /// control what GetUserIdByEmailAsync resolves for the retry.
    /// </summary>
    public string? EmailAlreadyRegisteredFor { get; set; }

    public bool ShouldThrowOnUpdatePassword { get; set; }

    public List<(string Email, string Password)> EnsuredDevUsers { get; } = [];
    public List<(string Email, string Password)> SignedInUsers { get; } = [];
    public List<(string Email, string Password)> ConfirmedUsersCreated { get; } = [];

    public List<Guid> DeletedUserIds { get; } = [];

    public Task<Guid> CreateUserAsync(
        string email, string password, string redirectTo, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (ShouldThrowOnCreate)
        {
            throw new InvalidOperationException("Simulated Supabase failure.");
        }

        CreatedUsers.Add((email, redirectTo));
        var userId = UserIdToReturn ?? Guid.NewGuid();
        if (metadata is { Count: > 0 })
            MetadataByEmail[email.Trim()] = metadata;

        return Task.FromResult(userId);
    }

    public Task DeleteUserAsync(Guid supabaseUserId, CancellationToken cancellationToken)
    {
        DeletedUserIds.Add(supabaseUserId);
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

    public Dictionary<string, Guid> UserIdsByEmail { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool ShouldThrowOnGetUserIdByEmail { get; set; }

    public Task<Guid?> GetUserIdByEmailAsync(string email, CancellationToken cancellationToken)
    {
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

    /// <summary>Ticket 12 (P1): records the caller-supplied metadata for a given email so
    /// <see cref="GetUserMetadataByEmailAsync"/> can prove/disprove a provisioning correlation
    /// value, mirroring the real gateway's user_metadata round trip.</summary>
    public Dictionary<string, IReadOnlyDictionary<string, string>> MetadataByEmail { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<Guid> CreateConfirmedUserAsync(
        string email, string password, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (ShouldThrowOnCreate)
        {
            throw new InvalidOperationException("Simulated Supabase failure.");
        }

        if (string.Equals(EmailAlreadyRegisteredFor, email, StringComparison.OrdinalIgnoreCase))
        {
            throw new EmailAlreadyRegisteredException(email);
        }

        ConfirmedUsersCreated.Add((email, password));
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

    public Task<SupabaseSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken)
    {
        if (ShouldThrowOnSignIn)
        {
            throw new InvalidOperationException("Simulated Supabase sign-in failure.");
        }

        SignedInUsers.Add((email, password));
        return Task.FromResult(new SupabaseSession("access-token", "refresh-token", UserIdToReturn ?? Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1)));
    }

    public List<(string Email, string RedirectTo, IReadOnlyDictionary<string, string> Metadata)> PendingUsersCreatedWithMetadata { get; } = [];

    public Task<Guid> CreatePendingUserWithMetadataAsync(
        string email, string redirectTo, IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken)
    {
        if (string.Equals(EmailAlreadyRegisteredFor, email, StringComparison.OrdinalIgnoreCase))
            throw new EmailAlreadyRegisteredException(email);

        if (ShouldThrowOnCreate)
            throw new InvalidOperationException("Simulated Supabase failure.");

        PendingUsersCreatedWithMetadata.Add((email, redirectTo, metadata));
        MetadataByEmail[email.Trim()] = metadata;

        var userId = UserIdToReturn ?? Guid.NewGuid();
        UserIdsByEmail[email.Trim()] = userId;
        return Task.FromResult(userId);
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
        EmailAlreadyRegisteredFor = null;
        UserIdsByEmail.Clear();
        PendingUsersCreatedWithMetadata.Clear();
        MetadataByEmail.Clear();
        ShouldThrowOnGetUserIdByEmail = false;
    }
}
