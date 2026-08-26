using HR.Modules.Identity.Domain;

namespace HR.Modules.Identity.Features.ListEmployeeRoleOverrides;

internal sealed record EmployeeRoleOverrideItem(
    Guid RoleId,
    EmployeeRoleOverrideType OverrideType,
    string Reason,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset AssignedAt,
    Guid? AssignedBy);

internal sealed record ListEmployeeRoleOverridesResponse(IReadOnlyList<EmployeeRoleOverrideItem> Overrides);
