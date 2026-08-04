namespace HR.Modules.Support.Domain;

internal sealed class SupportNotificationAttempt
{
    private SupportNotificationAttempt() { }

    public Guid Id { get; private set; }
    public Guid SupportRequestId { get; private set; }
    public Guid CompanyId { get; private set; }
    public SupportNotificationType NotificationType { get; private set; }
    public string RecipientEmail { get; private set; } = string.Empty;
    public SupportNotificationStatus Status { get; private set; }
    public DateTimeOffset AttemptedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int RetryCount { get; private set; }

    public static SupportNotificationAttempt Create(
        Guid id,
        Guid supportRequestId,
        Guid companyId,
        SupportNotificationType notificationType,
        string recipientEmail,
        DateTimeOffset now)
    {
        return new SupportNotificationAttempt
        {
            Id = id,
            SupportRequestId = supportRequestId,
            CompanyId = companyId,
            NotificationType = notificationType,
            RecipientEmail = recipientEmail,
            Status = SupportNotificationStatus.Pending,
            AttemptedAt = now,
            RetryCount = 0
        };
    }

    public void MarkSent(DateTimeOffset now)
    {
        Status = SupportNotificationStatus.Sent;
        SentAt = now;
        ErrorMessage = null;
    }

    public void MarkFailed(string errorMessage, DateTimeOffset now)
    {
        Status = SupportNotificationStatus.Failed;
        ErrorMessage = errorMessage;
        AttemptedAt = now;
    }

    public void IncrementRetry()
    {
        RetryCount++;
    }
}
