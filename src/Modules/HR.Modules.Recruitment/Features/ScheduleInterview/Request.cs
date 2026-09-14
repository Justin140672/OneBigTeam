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

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
