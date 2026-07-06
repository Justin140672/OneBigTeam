namespace HR.Modules.Recruitment.Features.ListCandidates;

internal sealed record ListCandidatesRequest
{
    public Guid CompanyId { get; init; }
    public string? Search { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
