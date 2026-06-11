namespace HR.Modules.Employees.Features.ListPositionProfiles;

internal sealed record ListPositionProfilesRequest
{
    public Guid CompanyId { get; init; }
    public bool IncludeInactive { get; init; } = false;
}
