namespace HR.Modules.Recruitment.Features.UpdateInterview;

internal sealed record UpdateInterviewRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public Guid ApplicationId { get; init; }
    public Guid InterviewId { get; init; }
    public Guid InterviewerEmployeeId { get; init; }
    public DateTimeOffset ScheduledAt { get; init; }
    public int? DurationMinutes { get; init; }
    public string? Location { get; init; }
}
