namespace HR.Modules.Recruitment.Features.ApplyPositionProfileMatches;

internal sealed record ApplyPositionProfileMatchesRequest
{
    public Guid CompanyId { get; init; }
}
