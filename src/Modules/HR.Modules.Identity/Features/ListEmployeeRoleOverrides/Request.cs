namespace HR.Modules.Identity.Features.ListEmployeeRoleOverrides;

internal sealed record ListEmployeeRoleOverridesRequest
{
    public Guid CompanyId { get; init; }
    public Guid UserId { get; init; }
}
