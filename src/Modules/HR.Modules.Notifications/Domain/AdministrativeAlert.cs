using HR.Infrastructure.Abstractions;

namespace HR.Modules.Notifications.Domain;

/// <summary>
/// ADM-03: a single de-duplicated administrative alert / incident in the shared admin inbox.
/// One live (non-resolved) row per (company, dedup key) — repeated occurrences of the same
/// underlying failure fold into <see cref="OccurrenceCount"/> / <see cref="LastOccurredAt"/>
/// rather than creating new rows. EF-materialized: private ctor, all private setters.
/// </summary>
internal sealed class AdministrativeAlert
{
    private AdministrativeAlert() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public AdministrativeAlertSeverity Severity { get; private set; }
    public AdministrativeAlertCategory Category { get; private set; }
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
        IsRead             = false,
        Status             = AdministrativeAlertStatus.Open,
        CreatedAt          = now,
    };

    public void RecordRecurrence(
        AdministrativeAlertSeverity severity,
        string summary,
        string? detail,
        DateTimeOffset occurredAt)
    {
        OccurrenceCount++;
        LastOccurredAt = occurredAt > LastOccurredAt ? occurredAt : LastOccurredAt;
        Severity = (AdministrativeAlertSeverity)Math.Max((int)Severity, (int)severity);
        Summary = summary;
        Detail = detail;
        IsRead = false;

        // A repeated failure re-opens an acknowledged alert. Resolved alerts are never recurred
        // here (the writer filters them out of the dedup lookup), so their status is left alone.
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
