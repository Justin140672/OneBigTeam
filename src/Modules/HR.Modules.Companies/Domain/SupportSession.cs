using HR.SharedKernel;

namespace HR.Modules.Companies.Domain;

/// <summary>
/// Time-boxed, single-use, revocable, audited grant of platform-administrator access to a
/// specific customer company's support context (Support epic, "Login As Customer"). Deliberately
/// company-scoped rather than tied to any specific customer user account — real user
/// impersonation would require minting Supabase-signed tokens for a real user, which this
/// codebase has no safe mechanism for (production auth is 100% real Supabase-issued JWTs). This
/// sidesteps that risk while still granting a platform administrator access to a customer's
/// environment for support purposes; no customer user's own identity, session, or audit trail is
/// ever touched.
/// </summary>
internal sealed class SupportSession
{
    private SupportSession() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid IssuedByAdminUserId { get; private set; }
    public string IssuedByAdminEmail { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RedeemedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public static SupportSession Issue(
        Guid companyId,
        Guid issuedByAdminUserId,
        string issuedByAdminEmail,
        string reason,
        string tokenHash,
        DateTimeOffset now)
    {
        return new SupportSession
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            IssuedByAdminUserId = issuedByAdminUserId,
            IssuedByAdminEmail = issuedByAdminEmail,
            Reason = reason,
            TokenHash = tokenHash,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(20),
        };
    }

    /// <summary>
    /// Point-in-time evaluation of persisted state (RedeemedAt/RevokedAt/ExpiresAt) — a computed
    /// property, not new state that itself needs persisting, matching the convention set by
    /// CustomerSubscription.MarkExpiredIfNeeded's remarks on persisted vs. derived state.
    /// </summary>
    public bool IsActive(DateTimeOffset now) => RedeemedAt is null && RevokedAt is null && now < ExpiresAt;

    public Result Redeem(DateTimeOffset now)
    {
        if (RedeemedAt is not null)
            return Result.Failure(Error.Validation("This support session has already been redeemed."));

        if (RevokedAt is not null)
            return Result.Failure(Error.Validation("This support session has been revoked."));

        if (now >= ExpiresAt)
            return Result.Failure(Error.Validation("This support session has expired."));

        RedeemedAt = now;
        return Result.Success();
    }

    public Result Revoke(DateTimeOffset now)
    {
        if (RedeemedAt is not null)
            return Result.Failure(Error.Validation("This support session has already been redeemed."));

        if (RevokedAt is not null)
            return Result.Failure(Error.Validation("This support session has already been revoked."));

        RevokedAt = now;
        return Result.Success();
    }
}
