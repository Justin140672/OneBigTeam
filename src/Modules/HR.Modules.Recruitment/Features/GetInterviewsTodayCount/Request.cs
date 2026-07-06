namespace HR.Modules.Recruitment.Features.GetInterviewsTodayCount;

internal sealed record GetInterviewsTodayCountRequest
{
    public Guid CompanyId { get; init; }
}
