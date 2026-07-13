namespace HR.Modules.Recruitment.Features.GetUpcomingInterviews;

internal sealed record GetUpcomingInterviewsRequest
{
    public Guid CompanyId { get; init; }
}
