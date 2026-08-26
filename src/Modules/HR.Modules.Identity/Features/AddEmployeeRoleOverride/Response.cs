using HR.Modules.Identity.Domain;

namespace HR.Modules.Identity.Features.AddEmployeeRoleOverride;

internal sealed record AddEmployeeRoleOverrideResponse(
    Guid UserId,
    Guid RoleId,
    EmployeeRoleOverrideType OverrideType,
    string Reason,
    DateTimeOffset? ExpiresAt);
