using HR.Infrastructure.Abstractions;

namespace HR.Modules.Notifications.Domain;

internal sealed class AdministrativeAlert
{
    private AdministrativeAlert() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public AdministrativeAlertSeverity Severity { get; private set; }
    public AdministrativeAlertCategory Category { get; private set; }

    public AdministrativeAlertReason? Reason { get; private set; }

    public string Summary { get; private set; } = string.Empty;
    public string? Detail { get; private set; }
    public string DedupKey { get; private set; } = string.Empty;
    public int OccurrenceCount { get; private set; }
    public DateTimeOffset FirstOccurredAt { get; private set; }
    public DateTimeOffset LastOccurredAt { get; private set; }
    public string? AffectedEntityType { get; private set; }
    public Guid? AffectedEntityId { get; private set; }
    public string? RecommendedAction { get; private set; }
    public string? ActionUrl { get; private set; }

    public int? AffectedItemCount { get; private set; }
    public bool IsRead { get; private set; }
    public AdministrativeAlertStatus Status { get; private set; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }
    public Guid? AcknowledgedByUserId { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public Guid? ResolvedByUserId { get; private set; }
    public string? ResolutionNote { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static AdministrativeAlert Raise(Guid id, RaiseAdministrativeAlertCommand command, DateTimeOffset now) => new()
    {
        Id                 = id,
        CompanyId          = command.CompanyId,
        Severity           = command.Severity,
        Category           = command.Category,
        Reason             = command.Reason,
        Summary            = command.Summary,
        Detail             = command.Detail,
        DedupKey           = command.DedupKey,
        OccurrenceCount    = 1,
        FirstOccurredAt    = command.OccurredAt,
        LastOccurredAt     = command.OccurredAt,
        AffectedEntityType = command.AffectedEntityType,
        AffectedEntityId   = command.AffectedEntityId,
        RecommendedAction  = command.RecommendedAction,
        ActionUrl          = command.ActionUrl,
        AffectedItemCount  = command.AffectedItemCount,
        IsRead             = false,
        Status             = AdministrativeAlertStatus.Open,
        CreatedAt          = now,
    };

    public void RecordRecurrence(
        AdministrativeAlertSeverity severity,
        string summary,
        string? detail,
        DateTimeOffset occurredAt,
        int? affectedItemCount = null)
    {
        OccurrenceCount++;
        LastOccurredAt = occurredAt > LastOccurredAt ? occurredAt : LastOccurredAt;
        Severity = (AdministrativeAlertSeverity)Math.Max((int)Severity, (int)severity);
        Summary = summary;
        Detail = detail;
        if (affectedItemCount is not null)
            AffectedItemCount = affectedItemCount;
        IsRead = false;

        if (Status == AdministrativeAlertStatus.Acknowledged)
            Status = AdministrativeAlertStatus.Open;
    }

    public void MarkAsRead() => IsRead = true;

    public void Acknowledge(Guid userId, DateTimeOffset now)
    {
        if (Status != AdministrativeAlertStatus.Open)
            throw new InvalidOperationException($"Only an open alert can be acknowledged (current status: {Status}).");

        Status = AdministrativeAlertStatus.Acknowledged;
        AcknowledgedAt = now;
        AcknowledgedByUserId = userId;
        IsRead = true;
    }

    public void Resolve(Guid userId, string? note, DateTimeOffset now)
    {
        if (Status == AdministrativeAlertStatus.Resolved)
            throw new InvalidOperationException("Alert is already resolved.");

        Status = AdministrativeAlertStatus.Resolved;
        ResolvedAt = now;
        ResolvedByUserId = userId;
        ResolutionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        IsRead = true;
    }
}
