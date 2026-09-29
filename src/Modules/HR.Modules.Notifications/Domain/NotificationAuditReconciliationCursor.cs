namespace HR.Modules.Notifications.Domain;

internal sealed class NotificationAuditReconciliationCursor
{
    private NotificationAuditReconciliationCursor() { }

    public Guid CompanyId { get; private set; }

    public DateTimeOffset LastScannedCreatedAt { get; private set; }

    public Guid LastScannedNotificationId { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static NotificationAuditReconciliationCursor Create(
        Guid companyId, DateTimeOffset lastScannedCreatedAt, Guid lastScannedNotificationId, DateTimeOffset now) => new()
    {
        CompanyId = companyId,
        LastScannedCreatedAt = lastScannedCreatedAt,
        LastScannedNotificationId = lastScannedNotificationId,
        UpdatedAt = now,
    };

    public void Advance(DateTimeOffset lastScannedCreatedAt, Guid lastScannedNotificationId, DateTimeOffset now)
    {
        LastScannedCreatedAt = lastScannedCreatedAt;
        LastScannedNotificationId = lastScannedNotificationId;
        UpdatedAt = now;
    }

    public void Reset(DateTimeOffset now)
    {
        LastScannedCreatedAt = default;
        LastScannedNotificationId = default;
        UpdatedAt = now;
    }
}
