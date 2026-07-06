namespace HR.Modules.Recruitment.Domain;

internal sealed class Interview
{
    private Interview() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Guid InterviewerEmployeeId { get; private set; }
    public DateTimeOffset ScheduledAt { get; private set; }
    public int? DurationMinutes { get; private set; }
    public string? Location { get; private set; }
    public InterviewOutcome Outcome { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Interview Create(
        Guid id,
        Guid companyId,
        Guid applicationId,
        Guid interviewerEmployeeId,
        DateTimeOffset scheduledAt,
        int? durationMinutes,
        string? location,
        DateTimeOffset now) => new()
    {
        Id                    = id,
        CompanyId             = companyId,
        ApplicationId         = applicationId,
        InterviewerEmployeeId = interviewerEmployeeId,
        ScheduledAt           = scheduledAt,
        DurationMinutes       = durationMinutes,
        Location              = string.IsNullOrWhiteSpace(location) ? null : location.Trim(),
        Outcome               = InterviewOutcome.Pending,
        CreatedAt             = now,
        UpdatedAt             = now,
    };

    public void UpdateDetails(
        Guid interviewerEmployeeId,
        DateTimeOffset scheduledAt,
        int? durationMinutes,
        string? location,
        DateTimeOffset now)
    {
        if (Outcome != InterviewOutcome.Pending)
            throw new InvalidOperationException($"Cannot update an interview with outcome '{Outcome}'.");

        InterviewerEmployeeId = interviewerEmployeeId;
        ScheduledAt            = scheduledAt;
        DurationMinutes        = durationMinutes;
        Location               = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
        UpdatedAt              = now;
    }

    public void RecordOutcome(InterviewOutcome outcome, string? notes, DateTimeOffset now)
    {
        if (Outcome != InterviewOutcome.Pending)
            throw new InvalidOperationException($"Cannot record an outcome for an interview with outcome '{Outcome}'.");

        if (outcome == InterviewOutcome.Pending)
            throw new InvalidOperationException("Cannot record an outcome of 'Pending'.");

        Outcome   = outcome;
        Notes     = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Outcome != InterviewOutcome.Pending)
            throw new InvalidOperationException($"Cannot cancel an interview with outcome '{Outcome}'.");

        Outcome   = InterviewOutcome.Cancelled;
        UpdatedAt = now;
    }
}
