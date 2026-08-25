namespace HR.Modules.Notifications.Domain;

/// <summary>
/// Tracks the asynchronous email delivery attempt(s) for a single Notification (NOT-02). Exactly
/// one EmailDelivery row is created per notification that is channel-eligible for email (see
/// NotificationChannelDefaults) — the notification's own Id doubles as this row's idempotency key
/// (IdempotencyKey == NotificationId, enforced by a unique index), so re-enqueuing the delivery job
/// for the same notification (e.g. a caller retrying after a crash) can never create a duplicate
/// delivery attempt record; EmailDeliveryJob additionally short-circuits as a no-op once Status is
/// Sent, covering the case where Postmark's call actually succeeded but the app crashed before the
/// Sent status was persisted.
/// </summary>
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

    /// <summary>
    /// NOT-03: which NotificationTemplate version (see NotificationTemplateCatalogue) rendered
    /// EmailSubject/EmailBody below, or null when this delivery was raised via the pre-existing
    /// non-templated WriteAsync path (EmailDeliveryJob falls back to Notification.Title/Body in
    /// that case). Lets a support engineer see exactly what template version produced a given
    /// historical email even after the catalogue's wording later changes.
    /// </summary>
    public int? TemplateVersion { get; private set; }

    /// <summary>Rendered email subject, set only for templated deliveries (see TemplateVersion).</summary>
    public string? EmailSubject { get; private set; }

    /// <summary>Rendered, HTML-encoded email body, set only for templated deliveries (see TemplateVersion).</summary>
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

    /// <summary>
    /// NOT-03: creates an EmailDelivery whose subject/body were already rendered from a
    /// NotificationTemplate (see NotificationTemplateRenderer) rather than being derived later, at
    /// send time, from the notification's own Title/Body — this preserves the exact rendered
    /// (HTML-encoded) content and records which template version produced it.
    /// </summary>
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

    /// <summary>Records that a real delivery attempt (an actual Postmark call) is being made.</summary>
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

    /// <summary>
    /// Marks this delivery as permanently failed. Reason must already be a short, sanitised,
    /// human-readable category (e.g. "Invalid recipient address", "Email provider error") — never
    /// a raw exception message or stack trace, which could leak internal detail into a
    /// support/reporting-visible record.
    /// </summary>
    public void MarkFailed(string reason) => (Status, FailureReason) = (EmailDeliveryStatus.Failed, reason);
}
