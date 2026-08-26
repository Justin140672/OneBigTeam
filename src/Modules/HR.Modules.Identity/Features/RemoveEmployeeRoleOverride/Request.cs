namespace HR.Modules.Identity.Features.RemoveEmployeeRoleOverride;

internal sealed record RemoveEmployeeRoleOverrideRequest
{
    public Guid CompanyId { get; init; }
    public Guid UserId { get; init; }
    public Guid RoleId { get; init; }
}
