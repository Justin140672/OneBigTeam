using HR.SharedKernel;

namespace HR.Modules.Identity.Domain;

/// <summary>
/// P1: durable provisioning-workflow states for a <see cref="PlatformAdministrator"/> row — see
/// PlatformAdministrator's remarks and CreatePlatformAdministratorHandler/ActivatePlatformAdministratorHandler.
/// </summary>
internal static class PlatformAdministratorProvisioningStatus
{
    /// <summary>A brand-new identity-provider account was created and an activation email sent;
    /// awaiting the recipient to confirm it and authenticate for the first time.</summary>
    public const string PendingProvisioning = "pending_provisioning";

    /// <summary>An identity-provider account already existed for this email; awaiting the
    /// recipient to authenticate with their EXISTING credentials to prove ownership before this
    /// local record is linked to it.</summary>
    public const string PendingLinkVerification = "pending_link_verification";

    /// <summary>Local record is fully linked to a verified identity-provider user id and usable.</summary>
    public const string Active = "active";

    /// <summary>The identity-provider call (creation or delivery) failed durably. The local record
    /// still exists (no duplicate will be created on retry) and can be retried or cancelled.</summary>
    public const string Failed = "failed";
}

// A platform-level administrator account — not tied to any company. This is the real
// authorization source for the Admin Portal's "administrator management" screen, replacing the
// static PlatformAdmin:AllowedEmails config allow-list for that screen only. Existing handlers
// elsewhere that still check the config allow-list are unaffected by this entity.
//
// P1: creation is a durable provisioning workflow, not a single insert — see
// PlatformAdministratorProvisioningStatus, CreatePlatformAdministratorHandler and
// ActivatePlatformAdministratorHandler. A row always exists in a well-defined state from the
// moment it is first persisted, so a crashed/retried creation can never silently duplicate a
// privileged account (enforced additionally by a unique DB index on Email and on
// SupabaseAuthUserId-when-not-null).
internal sealed class PlatformAdministrator : IVersionedAggregate
{
    private PlatformAdministrator() { }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;

    /// <summary>May be null if a Supabase Auth user has not yet been provisioned/linked for this administrator.</summary>
    public Guid? SupabaseAuthUserId { get; private set; }

    public PlatformAdministratorRole Role { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Null means system-seeded (e.g. bootstrap seeding from configuration).</summary>
    public Guid? CreatedByUserId { get; private set; }

    public DateTimeOffset? DisabledAt { get; private set; }
    public Guid? DisabledByUserId { get; private set; }

    /// <summary>One of <see cref="PlatformAdministratorProvisioningStatus"/>.</summary>
    public string ProvisioningStatus { get; private set; } = PlatformAdministratorProvisioningStatus.Active;

    /// <summary>
    /// Stamped into the new Supabase user's <c>user_metadata</c> at creation time (PendingProvisioning
    /// path only) so a retry that hits "email already registered" can PROVE this exact operation
    /// created that account before ever linking to it — mirrors InviteAcceptanceOperation's identical
    /// Ticket 12 pattern for tenant-user invites.
    /// </summary>
    public Guid? ProvisioningCorrelationId { get; private set; }

    public string? ProvisioningFailureReason { get; private set; }
    public DateTimeOffset? ProvisioningStartedAt { get; private set; }
    public DateTimeOffset? ProvisioningCompletedAt { get; private set; }

    /// <summary>
    /// Which provisioning path this row is on — set once by <see cref="BeginProvisioning"/> and
    /// never changed, so RetryPlatformAdministratorProvisioning can resume the SAME path after a
    /// Failed outcome rather than re-deriving it (im)precisely from the current status alone.
    /// </summary>
    public bool IsNewIdentityProviderAccount { get; private set; }

    // Optimistic-concurrency token (Ticket 2 convention) — guards ActivatePlatformAdministrator
    // against a replayed/concurrent double-activation of the same row.
    public int Version { get; private set; } = 1;

    public void IncrementVersion() => Version++;

    public static PlatformAdministrator Create(
        string email,
        PlatformAdministratorRole role,
        DateTimeOffset now,
        Guid? createdByUserId = null,
        Guid? supabaseAuthUserId = null)
    {
        return new PlatformAdministrator
        {
            Id = Guid.NewGuid(),
            Email = email.Trim().ToLowerInvariant(),
            SupabaseAuthUserId = supabaseAuthUserId,
            Role = role,
            IsEnabled = true,
            CreatedAt = now,
            CreatedByUserId = createdByUserId,
            // Bootstrap/seed/legacy callers (config allow-list seeding, and any pre-P1 row) are
            // considered already-active — only CreatePlatformAdministratorHandler's own call site
            // below overrides this to a Pending* status for the real provisioning workflow.
            ProvisioningStatus = PlatformAdministratorProvisioningStatus.Active,
        };
    }

    /// <summary>
    /// Starts the durable provisioning workflow for a freshly-created row (P1). Called immediately
    /// after <see cref="Create"/>, before this row is first saved, so it is persisted already in a
    /// well-defined Pending* state rather than defaulting to Active.
    /// </summary>
    public void BeginProvisioning(bool isNewIdentityProviderAccount, Guid correlationId, DateTimeOffset now)
    {
        ProvisioningStatus = isNewIdentityProviderAccount
            ? PlatformAdministratorProvisioningStatus.PendingProvisioning
            : PlatformAdministratorProvisioningStatus.PendingLinkVerification;
        IsNewIdentityProviderAccount = isNewIdentityProviderAccount;
        ProvisioningCorrelationId = correlationId;
        ProvisioningStartedAt = now;
    }

    /// <summary>
    /// Records that the identity-provider call (account creation or link-verification email
    /// delivery) succeeded — moves the row back to the correct Pending* status (using the
    /// persisted <see cref="IsNewIdentityProviderAccount"/> path) and clears any prior failure
    /// reason. A no-op status change for a first-time attempt (already Pending*); meaningful for
    /// RetryPlatformAdministratorProvisioning resuming from Failed.
    /// </summary>
    public void MarkProvisioningDelivered(DateTimeOffset now)
    {
        ProvisioningStatus = IsNewIdentityProviderAccount
            ? PlatformAdministratorProvisioningStatus.PendingProvisioning
            : PlatformAdministratorProvisioningStatus.PendingLinkVerification;
        ProvisioningFailureReason = null;
    }

    /// <summary>
    /// Records that the identity-provider call (account creation or link-verification email
    /// delivery) failed. The row remains a real, retryable record — never rolled back/deleted —
    /// so a caller can retry via RetryPlatformAdministratorProvisioning without risking a duplicate
    /// identity-provider account.
    /// </summary>
    public void MarkProvisioningFailed(string reason, DateTimeOffset now)
    {
        ProvisioningStatus = PlatformAdministratorProvisioningStatus.Failed;
        ProvisioningFailureReason = reason;
    }

    /// <summary>
    /// Completes provisioning once the recipient has proven control of the identity-provider
    /// account (by authenticating as it — see ActivatePlatformAdministratorHandler) by linking this
    /// row to their real, verified <paramref name="supabaseAuthUserId"/>.
    /// </summary>
    public Result CompleteProvisioning(Guid supabaseAuthUserId, DateTimeOffset now)
    {
        if (ProvisioningStatus == PlatformAdministratorProvisioningStatus.Active)
            return Result.Failure(Error.Conflict("This administrator account has already been activated."));

        if (!IsEnabled)
            return Result.Failure(Error.Conflict("This administrator invitation has been cancelled."));

        SupabaseAuthUserId = supabaseAuthUserId;
        ProvisioningStatus = PlatformAdministratorProvisioningStatus.Active;
        ProvisioningFailureReason = null;
        ProvisioningCompletedAt = now;
        return Result.Success();
    }

    public void Disable(DateTimeOffset now, Guid? actorUserId)
    {
        IsEnabled = false;
        DisabledAt = now;
        DisabledByUserId = actorUserId;
    }

    public void Enable(DateTimeOffset now)
    {
        IsEnabled = true;
        DisabledAt = null;
        DisabledByUserId = null;
    }

    public void AssignRole(PlatformAdministratorRole newRole)
    {
        Role = newRole;
    }

    /// <summary>
    /// Back-links this administrator to their identity-provider (Supabase Auth) user id once it is
    /// known — either the first time they authenticate (see GetPlatformAdminMe) or when resolved by
    /// email during an MFA reset. Mirrors <c>UserProfile.UpdateSupabaseAuthUserId</c>. No-op if it
    /// is already set to the same value.
    /// </summary>
    public void LinkSupabaseAuthUserId(Guid supabaseAuthUserId)
    {
        SupabaseAuthUserId = supabaseAuthUserId;
    }
}
