namespace HR.Admin.Web.Models;


public sealed record PlatformAdministratorSummary(
    Guid Id,
    string Email,
    string Role,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DisabledAt);

public sealed record ListPlatformAdministratorsResponse(IReadOnlyList<PlatformAdministratorSummary> Administrators);

public sealed record CreateAdministratorRequest(string Email, string Role);

public sealed record CreateAdministratorResponse(
    Guid Id,
    string Email,
    string Role,
    bool IsEnabled,
    DateTimeOffset CreatedAt);

public sealed record AdministratorIdRequest(Guid Id);

public sealed record AdministratorEnabledStateResponse(Guid Id, bool IsEnabled);

public sealed record AssignAdministratorRoleRequest(Guid Id, string Role);

public sealed record AssignAdministratorRoleResponse(Guid Id, string Role);

public sealed record ResetAdministratorMfaRequest(Guid Id, bool Confirmed, string Reason);

public sealed record ResetAdministratorMfaResponse(
    Guid AdministratorId,
    string AdministratorEmail,
    int FactorsRemoved,
    bool NotificationDelivered);

public sealed record ResetAdministratorPasswordResponse(Guid Id, bool Requested);
