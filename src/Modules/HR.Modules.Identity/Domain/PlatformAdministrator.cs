using HR.SharedKernel;

namespace HR.Modules.Identity.Domain;

internal static class PlatformAdministratorProvisioningStatus
{
    public const string PendingProvisioning = "pending_provisioning";

    public const string PendingLinkVerification = "pending_link_verification";

    public const string Active = "active";

    public const string Failed = "failed";
}

internal sealed class PlatformAdministrator : IVersionedAggregate
{
    private PlatformAdministrator() { }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;

    public Guid? SupabaseAuthUserId { get; private set; }

    public PlatformAdministratorRole Role { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedByUserId { get; private set; }

    public DateTimeOffset? DisabledAt { get; private set; }
    public Guid? DisabledByUserId { get; private set; }

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
            ProvisioningStatus = PlatformAdministratorProvisioningStatus.Active,
        };
    }

    public void BeginProvisioning(bool isNewIdentityProviderAccount, Guid correlationId, DateTimeOffset now)
    {
        ProvisioningStatus = isNewIdentityProviderAccount
            ? PlatformAdministratorProvisioningStatus.PendingProvisioning
            : PlatformAdministratorProvisioningStatus.PendingLinkVerification;
        IsNewIdentityProviderAccount = isNewIdentityProviderAccount;
        ProvisioningCorrelationId = correlationId;
        ProvisioningStartedAt = now;
    }

    public void MarkProvisioningDelivered(DateTimeOffset now)
    {
        ProvisioningStatus = IsNewIdentityProviderAccount
            ? PlatformAdministratorProvisioningStatus.PendingProvisioning
            : PlatformAdministratorProvisioningStatus.PendingLinkVerification;
        ProvisioningFailureReason = null;
    }

    public void MarkProvisioningFailed(string reason, DateTimeOffset now)
    {
        ProvisioningStatus = PlatformAdministratorProvisioningStatus.Failed;
        ProvisioningFailureReason = reason;
    }

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

    public void LinkSupabaseAuthUserId(Guid supabaseAuthUserId)
    {
        SupabaseAuthUserId = supabaseAuthUserId;
    }
}
