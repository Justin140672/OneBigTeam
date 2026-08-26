using HR.Modules.Identity.Domain;

namespace HR.Modules.Identity.Features.AddEmployeeRoleOverride;

internal sealed record AddEmployeeRoleOverrideRequest
{
    public Guid CompanyId { get; init; }
    public Guid UserId { get; init; }
    public Guid RoleId { get; init; }
    public EmployeeRoleOverrideType OverrideType { get; init; }
    public string Reason { get; init; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; init; }
}
