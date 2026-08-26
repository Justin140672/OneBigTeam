using HR.Modules.Identity.Domain;
using HR.SharedKernel;

namespace HR.Modules.Identity;

// Published when an employee is invited to become a system user (Features/InviteEmployeeUser).
internal sealed record UserInvitedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid InviteId,
    string Email,
    IReadOnlyList<Guid> RoleIds,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType       => "user.invited";
    string IAuditEvent.EntityType      => "UserInvite";
    Guid   IAuditEvent.EntityId        => InviteId;
    Guid?  IAuditEvent.EmployeeId      => EmployeeId;
    Guid?  IAuditEvent.ActorUserId     => ActorUserId;
    Guid?  IAuditEvent.ActorEmployeeId => null;
    Guid?  IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary        => $"Invited {Email} to the platform";
    object? IAuditEvent.Before         => null;
    object? IAuditEvent.After          => new { Email, RoleIds };
    object? IAuditEvent.Metadata       => null;
}

// Published when a pending invite is resent (Features/ResendInvite) — token/expiry regenerated.
internal sealed record UserInviteResentAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid InviteId,
    string Email,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType       => "user-invite.resent";
    string IAuditEvent.EntityType      => "UserInvite";
    Guid   IAuditEvent.EntityId        => InviteId;
    Guid?  IAuditEvent.EmployeeId      => EmployeeId;
    Guid?  IAuditEvent.ActorUserId     => ActorUserId;
    Guid?  IAuditEvent.ActorEmployeeId => null;
    Guid?  IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary        => $"Resent invitation to {Email}";
    object? IAuditEvent.Before         => null;
    object? IAuditEvent.After          => null;
    object? IAuditEvent.Metadata       => null;
}

// Published when a pending invite is cancelled (Features/CancelInvite).
internal sealed record UserInviteCancelledAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid InviteId,
    string Email,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType       => "user-invite.cancelled";
    string IAuditEvent.EntityType      => "UserInvite";
    Guid   IAuditEvent.EntityId        => InviteId;
    Guid?  IAuditEvent.EmployeeId      => EmployeeId;
    Guid?  IAuditEvent.ActorUserId     => ActorUserId;
    Guid?  IAuditEvent.ActorEmployeeId => null;
    Guid?  IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary        => $"Cancelled invitation for {Email}";
    object? IAuditEvent.Before         => null;
    object? IAuditEvent.After          => null;
    object? IAuditEvent.Metadata       => null;
}

// Published when a user's role set changes (Features/UpdateUserRoles).
internal sealed record UserRolesChangedAuditEvent(
    Guid CompanyId,
    Guid UserId,
    Guid EmployeeId,
    IReadOnlyList<Guid> BeforeRoleIds,
    IReadOnlyList<Guid> AfterRoleIds,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType       => "user.roles-changed";
    string IAuditEvent.EntityType      => "ApplicationUser";
    Guid   IAuditEvent.EntityId        => UserId;
    Guid?  IAuditEvent.EmployeeId      => EmployeeId;
    Guid?  IAuditEvent.ActorUserId     => ActorUserId;
    Guid?  IAuditEvent.ActorEmployeeId => null;
    Guid?  IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary        => "User roles updated";
    object? IAuditEvent.Before         => new { RoleIds = BeforeRoleIds };
    object? IAuditEvent.After          => new { RoleIds = AfterRoleIds };
    object? IAuditEvent.Metadata       => null;
}

// IAM-02: published when a role-change request is rejected by a server-side safeguard
// (self-elevation, granting/revoking a role the actor is not authorised to administer, attempting
// to remove the mandatory Employee role, or attempting to remove/disable the last active holder of
// a lockout-protected role). CompanyId/UserId identify the target of the attempted change;
// ActorUserId identifies who attempted it. Reason is a short machine-readable code, not raw request
// payload, so this never leaks sensitive data into the audit trail.
internal sealed record RoleChangeRejectedAuditEvent(
    Guid CompanyId,
    Guid UserId,
    Guid EmployeeId,
    string Reason,
    IReadOnlyList<Guid> RequestedRoleIds,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType       => "user.role-change-rejected";
    string IAuditEvent.EntityType      => "ApplicationUser";
    Guid   IAuditEvent.EntityId        => UserId;
    Guid?  IAuditEvent.EmployeeId      => EmployeeId;
    Guid?  IAuditEvent.ActorUserId     => ActorUserId;
    Guid?  IAuditEvent.ActorEmployeeId => null;
    Guid?  IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary        => $"Rejected role change: {Reason}";
    object? IAuditEvent.Before         => null;
    object? IAuditEvent.After          => new { RequestedRoleIds };
    object? IAuditEvent.Metadata       => new { Reason };
}

// Published when a user account is manually disabled by an administrator (Features/DisableUser).
internal sealed record UserDisabledAuditEvent(
    Guid CompanyId,
    Guid UserId,
    Guid EmployeeId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType       => "user.disabled";
    string IAuditEvent.EntityType      => "ApplicationUser";
    Guid   IAuditEvent.EntityId        => UserId;
    Guid?  IAuditEvent.EmployeeId      => EmployeeId;
    Guid?  IAuditEvent.ActorUserId     => ActorUserId;
    Guid?  IAuditEvent.ActorEmployeeId => null;
    Guid?  IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary        => "User account disabled";
    object? IAuditEvent.Before         => new { IsActive = true };
    object? IAuditEvent.After          => new { IsActive = false };
    object? IAuditEvent.Metadata       => null;
}

// Published when a user account is automatically disabled because the linked employee's
// offboarding plan completed (Features/OnOffboardingPlanCompleted). Tagged distinctly from the
// manual UserDisabledAuditEvent above so audit history can tell the two apart.
internal sealed record UserAutoDisabledOnOffboardingAuditEvent(
    Guid CompanyId,
    Guid UserId,
    Guid EmployeeId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType       => "user.auto-disabled-offboarding";
    string IAuditEvent.EntityType      => "ApplicationUser";
    Guid   IAuditEvent.EntityId        => UserId;
    Guid?  IAuditEvent.EmployeeId      => EmployeeId;
    Guid?  IAuditEvent.ActorUserId     => null;
    Guid?  IAuditEvent.ActorEmployeeId => null;
    Guid?  IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary        => "User account automatically disabled — offboarding plan completed";
    object? IAuditEvent.Before         => new { IsActive = true };
    object? IAuditEvent.After          => new { IsActive = false };
    object? IAuditEvent.Metadata       => null;
}

// Published when a user account is re-enabled by an administrator (Features/EnableUser).
internal sealed record UserEnabledAuditEvent(
    Guid CompanyId,
    Guid UserId,
    Guid EmployeeId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType       => "user.enabled";
    string IAuditEvent.EntityType      => "ApplicationUser";
    Guid   IAuditEvent.EntityId        => UserId;
    Guid?  IAuditEvent.EmployeeId      => EmployeeId;
    Guid?  IAuditEvent.ActorUserId     => ActorUserId;
    Guid?  IAuditEvent.ActorEmployeeId => null;
    Guid?  IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary        => "User account re-enabled";
    object? IAuditEvent.Before         => new { IsActive = false };
    object? IAuditEvent.After          => new { IsActive = true };
    object? IAuditEvent.Metadata       => null;
}

// IAM-03: published when an administrator changes a position's default role set
// (Features/SetPositionRoleDefaults). EntityId is the Position (== owning PositionProfile) id.
internal sealed record PositionRoleDefaultsChangedAuditEvent(
    Guid CompanyId,
    Guid PositionId,
    IReadOnlyList<Guid> BeforeRoleIds,
    IReadOnlyList<Guid> AfterRoleIds,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType       => "position.role-defaults-changed";
    string IAuditEvent.EntityType      => "Position";
    Guid   IAuditEvent.EntityId        => PositionId;
    Guid?  IAuditEvent.ActorUserId     => ActorUserId;
    Guid?  IAuditEvent.ActorEmployeeId => null;
    Guid?  IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary        => "Position default roles updated";
    object? IAuditEvent.Before         => new { RoleIds = BeforeRoleIds };
    object? IAuditEvent.After          => new { RoleIds = AfterRoleIds };
    object? IAuditEvent.Metadata       => null;
}

// IAM-03: published when an employee's position assignment changes (new hire or transfer) and the
// resulting change in inherited roles is applied to identity.user_positions. ActorUserId is always
// null — the triggering integration events (EmployeeCreatedIntegrationEvent,
// EmployeePositionChangedIntegrationEvent) do not carry the acting HR administrator's id; that
// attribution already exists on the Employees module's own audit trail for the profile/create
// action (EmployeeProfileUpdatedAuditEvent etc.) and can be cross-referenced by EmployeeId + time.
internal sealed record EmployeeInheritedRolesRecalculatedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid? PreviousPositionId,
    Guid? NewPositionId,
    IReadOnlyList<Guid> BeforeRoleIds,
    IReadOnlyList<Guid> AfterRoleIds,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType       => "employee.inherited-roles-recalculated";
    string IAuditEvent.EntityType      => "UserPosition";
    Guid   IAuditEvent.EntityId        => EmployeeId;
    Guid?  IAuditEvent.EmployeeId      => EmployeeId;
    Guid?  IAuditEvent.ActorUserId     => null;
    Guid?  IAuditEvent.ActorEmployeeId => null;
    Guid?  IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary        => "Inherited roles recalculated following a position assignment change";
    object? IAuditEvent.Before         => new { PositionId = PreviousPositionId, RoleIds = BeforeRoleIds };
    object? IAuditEvent.After          => new { PositionId = NewPositionId, RoleIds = AfterRoleIds };
    object? IAuditEvent.Metadata       => null;
}

// IAM-04: published when an administrator creates (or replaces) an employee-level role override
// (Features/AddEmployeeRoleOverride).
internal sealed record EmployeeRoleOverrideCreatedAuditEvent(
    Guid CompanyId,
    Guid UserId,
    Guid OverrideId,
    Guid RoleId,
    EmployeeRoleOverrideType OverrideType,
    string Reason,
    DateTimeOffset? ExpiresAt,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType       => "user.role-override-created";
    string IAuditEvent.EntityType      => "EmployeeRoleOverride";
    Guid   IAuditEvent.EntityId        => OverrideId;
    Guid?  IAuditEvent.EmployeeId      => UserId;
    Guid?  IAuditEvent.ActorUserId     => ActorUserId;
    Guid?  IAuditEvent.ActorEmployeeId => null;
    Guid?  IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary        => $"Created {OverrideType} override for role {RoleId}: {Reason}";
    object? IAuditEvent.Before         => null;
    object? IAuditEvent.After          => new { RoleId, OverrideType, Reason, ExpiresAt };
    object? IAuditEvent.Metadata       => null;
}

// IAM-04: published when an administrator removes an employee-level role override
// (Features/RemoveEmployeeRoleOverride).
internal sealed record EmployeeRoleOverrideRemovedAuditEvent(
    Guid CompanyId,
    Guid UserId,
    Guid OverrideId,
    Guid RoleId,
    EmployeeRoleOverrideType OverrideType,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType       => "user.role-override-removed";
    string IAuditEvent.EntityType      => "EmployeeRoleOverride";
    Guid   IAuditEvent.EntityId        => OverrideId;
    Guid?  IAuditEvent.EmployeeId      => UserId;
    Guid?  IAuditEvent.ActorUserId     => ActorUserId;
    Guid?  IAuditEvent.ActorEmployeeId => null;
    Guid?  IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary        => $"Removed {OverrideType} override for role {RoleId}";
    object? IAuditEvent.Before         => new { RoleId, OverrideType };
    object? IAuditEvent.After          => null;
    object? IAuditEvent.Metadata       => null;
}

// IAM-04: published by the daily sweep (Jobs/ExpireEmployeeRoleOverridesJob) when a temporary
// override's ExpiresAt has passed and it is cleared out — distinct from a manual removal so audit
// history can tell the two apart. ActorUserId is always null (system-driven, not an administrator
// action).
internal sealed record EmployeeRoleOverrideExpiredAuditEvent(
    Guid CompanyId,
    Guid UserId,
    Guid OverrideId,
    Guid RoleId,
    EmployeeRoleOverrideType OverrideType,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType       => "user.role-override-expired";
    string IAuditEvent.EntityType      => "EmployeeRoleOverride";
    Guid   IAuditEvent.EntityId        => OverrideId;
    Guid?  IAuditEvent.EmployeeId      => UserId;
    Guid?  IAuditEvent.ActorUserId     => null;
    Guid?  IAuditEvent.ActorEmployeeId => null;
    Guid?  IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary        => $"{OverrideType} override for role {RoleId} expired";
    object? IAuditEvent.Before         => new { RoleId, OverrideType };
    object? IAuditEvent.After          => null;
    object? IAuditEvent.Metadata       => null;
}

// Platform administrator management (Admin Portal "administrator management" screen). These
// events cover a platform-level concept with no company relationship — CompanyId is always
// Guid.Empty since IAuditEvent requires a non-nullable CompanyId.

// Published when a new platform administrator account is created (Features/CreatePlatformAdministrator).
internal sealed record PlatformAdministratorCreatedAuditEvent(
    Guid AdministratorId,
    string Email,
    PlatformAdministratorRole Role,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    Guid    IAuditEvent.CompanyId      => Guid.Empty;
    string  IAuditEvent.EventType      => "platform-administrator.created";
    string  IAuditEvent.EntityType     => "PlatformAdministrator";
    Guid    IAuditEvent.EntityId       => AdministratorId;
    Guid?   IAuditEvent.ActorUserId    => ActorUserId;
    Guid?   IAuditEvent.ActorEmployeeId => null;
    Guid?   IAuditEvent.CorrelationId  => null;
    string? IAuditEvent.Summary        => $"Created platform administrator account for {Email}";
    object? IAuditEvent.Before         => null;
    object? IAuditEvent.After          => new { Email, Role };
    object? IAuditEvent.Metadata       => null;
}

// Published when a platform administrator account is disabled (Features/DisablePlatformAdministrator).
internal sealed record PlatformAdministratorDisabledAuditEvent(
    Guid AdministratorId,
    string Email,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    Guid    IAuditEvent.CompanyId      => Guid.Empty;
    string  IAuditEvent.EventType      => "platform-administrator.disabled";
    string  IAuditEvent.EntityType     => "PlatformAdministrator";
    Guid    IAuditEvent.EntityId       => AdministratorId;
    Guid?   IAuditEvent.ActorUserId    => ActorUserId;
    Guid?   IAuditEvent.ActorEmployeeId => null;
    Guid?   IAuditEvent.CorrelationId  => null;
    string? IAuditEvent.Summary        => $"Disabled platform administrator account for {Email}";
    object? IAuditEvent.Before         => new { IsEnabled = true };
    object? IAuditEvent.After          => new { IsEnabled = false };
    object? IAuditEvent.Metadata       => null;
}

// Published when a platform administrator account is re-enabled (Features/EnablePlatformAdministrator).
internal sealed record PlatformAdministratorEnabledAuditEvent(
    Guid AdministratorId,
    string Email,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    Guid    IAuditEvent.CompanyId      => Guid.Empty;
    string  IAuditEvent.EventType      => "platform-administrator.enabled";
    string  IAuditEvent.EntityType     => "PlatformAdministrator";
    Guid    IAuditEvent.EntityId       => AdministratorId;
    Guid?   IAuditEvent.ActorUserId    => ActorUserId;
    Guid?   IAuditEvent.ActorEmployeeId => null;
    Guid?   IAuditEvent.CorrelationId  => null;
    string? IAuditEvent.Summary        => $"Re-enabled platform administrator account for {Email}";
    object? IAuditEvent.Before         => new { IsEnabled = false };
    object? IAuditEvent.After          => new { IsEnabled = true };
    object? IAuditEvent.Metadata       => null;
}

// Published when a platform administrator's role changes (Features/AssignPlatformAdministratorRole).
internal sealed record PlatformAdministratorRoleAssignedAuditEvent(
    Guid AdministratorId,
    string Email,
    PlatformAdministratorRole BeforeRole,
    PlatformAdministratorRole AfterRole,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    Guid    IAuditEvent.CompanyId      => Guid.Empty;
    string  IAuditEvent.EventType      => "platform-administrator.role-assigned";
    string  IAuditEvent.EntityType     => "PlatformAdministrator";
    Guid    IAuditEvent.EntityId       => AdministratorId;
    Guid?   IAuditEvent.ActorUserId    => ActorUserId;
    Guid?   IAuditEvent.ActorEmployeeId => null;
    Guid?   IAuditEvent.CorrelationId  => null;
    string? IAuditEvent.Summary        => $"Changed platform administrator role for {Email}";
    object? IAuditEvent.Before         => new { Role = BeforeRole };
    object? IAuditEvent.After          => new { Role = AfterRole };
    object? IAuditEvent.Metadata       => null;
}

// Published when a password reset email is requested for a platform administrator
// (Features/ResetPlatformAdministratorPassword).
internal sealed record PlatformAdministratorPasswordResetRequestedAuditEvent(
    Guid AdministratorId,
    string Email,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    Guid    IAuditEvent.CompanyId      => Guid.Empty;
    string  IAuditEvent.EventType      => "platform-administrator.password-reset-requested";
    string  IAuditEvent.EntityType     => "PlatformAdministrator";
    Guid    IAuditEvent.EntityId       => AdministratorId;
    Guid?   IAuditEvent.ActorUserId    => ActorUserId;
    Guid?   IAuditEvent.ActorEmployeeId => null;
    Guid?   IAuditEvent.CorrelationId  => null;
    string? IAuditEvent.Summary        => $"Requested password reset for platform administrator {Email}";
    object? IAuditEvent.Before         => null;
    object? IAuditEvent.After          => null;
    object? IAuditEvent.Metadata       => null;
}

// Published when an MFA reset is requested for a platform administrator
// (Features/ResetPlatformAdministratorMfa). Deliberately stubbed — see the handler's remarks —
// so the Summary makes clear the underlying action is not yet actually wired up.
internal sealed record PlatformAdministratorMfaResetRequestedAuditEvent(
    Guid AdministratorId,
    string Email,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    Guid    IAuditEvent.CompanyId      => Guid.Empty;
    string  IAuditEvent.EventType      => "platform-administrator.mfa-reset-requested";
    string  IAuditEvent.EntityType     => "PlatformAdministrator";
    Guid    IAuditEvent.EntityId       => AdministratorId;
    Guid?   IAuditEvent.ActorUserId    => ActorUserId;
    Guid?   IAuditEvent.ActorEmployeeId => null;
    Guid?   IAuditEvent.CorrelationId  => null;
    string? IAuditEvent.Summary        => $"MFA reset requested (not yet implemented) for platform administrator {Email}";
    object? IAuditEvent.Before         => null;
    object? IAuditEvent.After          => null;
    object? IAuditEvent.Metadata       => null;
}
