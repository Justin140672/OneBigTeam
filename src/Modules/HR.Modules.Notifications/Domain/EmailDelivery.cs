namespace HR.Modules.Notifications.Domain;

internal sealed class EmailDelivery
{
    private EmailDelivery() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid NotificationId { get; private set; }
    public Guid IdempotencyKey { get; private set; }
    public EmailDeliveryStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public int? TemplateVersion { get; private set; }

    public string? EmailSubject { get; private set; }

    public string? EmailBody { get; private set; }

    public static EmailDelivery Create(
        Guid id, Guid companyId, Guid notificationId, DateTimeOffset now) => new()
    {
        Id             = id,
        CompanyId      = companyId,
        NotificationId = notificationId,
        IdempotencyKey = notificationId,
        Status         = EmailDeliveryStatus.Pending,
        AttemptCount   = 0,
        CreatedAt      = now,
    };

    public static EmailDelivery CreateTemplated(
        Guid id, Guid companyId, Guid notificationId, int templateVersion,
        string emailSubject, string emailBody, DateTimeOffset now) => new()
    {
        Id              = id,
        CompanyId       = companyId,
        NotificationId  = notificationId,
        IdempotencyKey  = notificationId,
        Status          = EmailDeliveryStatus.Pending,
        AttemptCount    = 0,
        TemplateVersion = templateVersion,
        EmailSubject    = emailSubject,
        EmailBody       = emailBody,
        CreatedAt       = now,
    };

    public void RecordAttempt(DateTimeOffset now)
    {
        AttemptCount++;
        LastAttemptAt = now;
    }

    public void MarkSent(DateTimeOffset now)
    {
        Status = EmailDeliveryStatus.Sent;
        SentAt = now;
        FailureReason = null;
    }

    public void MarkFailed(string reason) => (Status, FailureReason) = (EmailDeliveryStatus.Failed, reason);

    public void MarkSkipped(string reason) => (Status, FailureReason) = (EmailDeliveryStatus.Skipped, reason);
}
