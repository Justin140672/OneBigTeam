namespace HR.Modules.Identity.Features.UpdateUserRoles;

internal sealed record UpdateUserRolesRequest
{
    public Guid CompanyId { get; init; }
    public Guid UserId { get; init; }
    public List<Guid> RoleIds { get; init; } = [];
}
