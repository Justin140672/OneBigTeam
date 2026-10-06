namespace HR.Modules.Recruitment.Domain;

internal sealed class InternalOfferTaskEffect
{
    private InternalOfferTaskEffect() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ApplicationId { get; private set; }
    public int OfferVersion { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid MadeByUserId { get; private set; }
    public string JobTitle { get; private set; } = string.Empty;
    public DateOnly? ResponseDeadline { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? TaskCreatedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public DateTimeOffset? ResponseNotifiedAt { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public string? FailureReason { get; private set; }

    public static InternalOfferTaskEffect Create(
        Guid id, Guid companyId, Guid applicationId, int offerVersion, Guid employeeId,
        Guid madeByUserId, string jobTitle, DateOnly? responseDeadline, DateTimeOffset now) =>
        new()
        {
            Id = id,
            CompanyId = companyId,
            ApplicationId = applicationId,
            OfferVersion = offerVersion,
            EmployeeId = employeeId,
            MadeByUserId = madeByUserId,
            JobTitle = jobTitle,
            ResponseDeadline = responseDeadline,
            CreatedAt = now,
        };

    public void MarkTaskCreated(DateTimeOffset now)
    {
        TaskCreatedAt ??= now;
        Touch(now);
    }

    public void MarkClosed(DateTimeOffset now)
    {
        ClosedAt = now;
        Touch(now);
    }

    public void MarkResponseNotified(DateTimeOffset now)
    {
        ResponseNotifiedAt = now;
        Touch(now);
    }

    public void MarkFailed(string reason, DateTimeOffset now)
    {
        AttemptCount++;
        LastAttemptAt = now;
        FailureReason = reason.Length > 500 ? reason[..500] : reason;
    }

    private void Touch(DateTimeOffset now)
    {
        AttemptCount++;
        LastAttemptAt = now;
        FailureReason = null;
    }
}
