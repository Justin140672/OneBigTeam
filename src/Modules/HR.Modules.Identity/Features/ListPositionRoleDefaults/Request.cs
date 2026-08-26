namespace HR.Modules.Identity.Features.ListPositionRoleDefaults;

internal sealed record ListPositionRoleDefaultsRequest
{
    public Guid CompanyId { get; init; }
}
