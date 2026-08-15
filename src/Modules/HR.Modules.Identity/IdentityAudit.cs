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
