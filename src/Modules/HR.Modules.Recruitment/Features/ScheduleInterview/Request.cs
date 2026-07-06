namespace HR.Modules.Recruitment.Features.ScheduleInterview;

internal sealed record ScheduleInterviewRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public Guid ApplicationId { get; init; }
    public Guid InterviewerEmployeeId { get; init; }
    public DateTimeOffset ScheduledAt { get; init; }
    public int? DurationMinutes { get; init; }
    public string? Location { get; init; }
}
