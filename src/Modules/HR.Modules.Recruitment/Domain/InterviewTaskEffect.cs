namespace HR.Modules.Recruitment.Domain;

internal sealed class InterviewTaskEffect
{
    private InterviewTaskEffect() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Guid InterviewId { get; private set; }
    public Guid ScheduledBy { get; private set; }
    public Guid InterviewerEmployeeId { get; private set; }
    public DateTimeOffset ScheduledAt { get; private set; }
    public string CandidateName { get; private set; } = string.Empty;
    public string VacancyTitle { get; private set; } = string.Empty;
    public int AttemptCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public static InterviewTaskEffect Create(
        Guid id, Guid companyId, Guid applicationId, Guid interviewId, Guid scheduledBy,
        Guid interviewerEmployeeId, DateTimeOffset scheduledAt, string candidateName, string vacancyTitle,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            CompanyId = companyId,
            ApplicationId = applicationId,
            InterviewId = interviewId,
            ScheduledBy = scheduledBy,
            InterviewerEmployeeId = interviewerEmployeeId,
            ScheduledAt = scheduledAt,
            CandidateName = candidateName,
            VacancyTitle = vacancyTitle,
            CreatedAt = now,
        };

    public void MarkCompleted(DateTimeOffset now)
    {
        AttemptCount++;
        LastAttemptAt = now;
        CompletedAt = now;
        FailureReason = null;
    }

    public void MarkFailed(string reason, DateTimeOffset now)
    {
        AttemptCount++;
        LastAttemptAt = now;
        FailureReason = reason.Length > 500 ? reason[..500] : reason;
    }
}
