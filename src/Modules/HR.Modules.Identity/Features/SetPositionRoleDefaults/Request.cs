namespace HR.Modules.Identity.Features.SetPositionRoleDefaults;

internal sealed record SetPositionRoleDefaultsRequest
{
    public Guid CompanyId { get; init; }
    public Guid PositionProfileId { get; init; }
    public List<Guid> RoleIds { get; init; } = [];
}
