namespace HR.Admin.Web.Models;

// Mirrors HR.Modules.Identity.Features.ListPlatformAdministrators/CreatePlatformAdministrator/etc.
// response shapes exactly — same "app-local DTO matching the API contract" convention as
// CustomerDetailsModels.cs. HR.Admin.Web does not reference HR.Modules.Identity (or configure a
// JsonStringEnumConverter on its own HttpClient's default JSON options), so
// PlatformAdministratorRole is modeled here as a plain string ("SupportStaff" | "PlatformOwner")
// and rendered/parsed as-is, rather than as a C# enum.

/// <summary>Mirrors ListPlatformAdministrators.PlatformAdministratorSummary.</summary>
public sealed record PlatformAdministratorSummary(
    Guid Id,
    string Email,
    string Role,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DisabledAt);

/// <summary>Mirrors ListPlatformAdministratorsResponse.</summary>
public sealed record ListPlatformAdministratorsResponse(IReadOnlyList<PlatformAdministratorSummary> Administrators);

/// <summary>Mirrors CreatePlatformAdministratorRequest.</summary>
public sealed record CreateAdministratorRequest(string Email, string Role);

/// <summary>Mirrors CreatePlatformAdministratorResponse.</summary>
public sealed record CreateAdministratorResponse(
    Guid Id,
    string Email,
    string Role,
    bool IsEnabled,
    DateTimeOffset CreatedAt);

/// <summary>Mirrors DisablePlatformAdministratorRequest / EnablePlatformAdministratorRequest.</summary>
public sealed record AdministratorIdRequest(Guid Id);

/// <summary>Mirrors DisablePlatformAdministratorResponse / EnablePlatformAdministratorResponse.</summary>
public sealed record AdministratorEnabledStateResponse(Guid Id, bool IsEnabled);

/// <summary>Mirrors AssignPlatformAdministratorRoleRequest.</summary>
public sealed record AssignAdministratorRoleRequest(Guid Id, string Role);

/// <summary>Mirrors AssignPlatformAdministratorRoleResponse.</summary>
public sealed record AssignAdministratorRoleResponse(Guid Id, string Role);

/// <summary>Mirrors ResetPlatformAdministratorMfaRequest ({ id, confirmed, reason }).</summary>
public sealed record ResetAdministratorMfaRequest(Guid Id, bool Confirmed, string Reason);

/// <summary>
/// Mirrors ResetPlatformAdministratorMfaResponse. Returned after a real MFA reset: every
/// multi-factor factor for the administrator is removed from the identity provider,
/// FactorsRemoved reports how many were removed, and NotificationDelivered indicates whether
/// the notification email was sent.
/// </summary>
public sealed record ResetAdministratorMfaResponse(
    Guid AdministratorId,
    string AdministratorEmail,
    int FactorsRemoved,
    bool NotificationDelivered);

/// <summary>Mirrors ResetPlatformAdministratorPasswordResponse. This one is fully implemented (sends a real Supabase password-recovery email).</summary>
public sealed record ResetAdministratorPasswordResponse(Guid Id, bool Requested);
