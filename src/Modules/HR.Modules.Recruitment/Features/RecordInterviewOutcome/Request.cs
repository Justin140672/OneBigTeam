using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.RecordInterviewOutcome;

internal sealed record RecordInterviewOutcomeRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public Guid ApplicationId { get; init; }
    public Guid InterviewId { get; init; }
    public InterviewOutcome Outcome { get; init; }
    public string? Notes { get; init; }
}
