namespace HR.Infrastructure.Abstractions;

public sealed record RaiseAdministrativeAlertCommand(
    Guid CompanyId,
    AdministrativeAlertSeverity Severity,
    AdministrativeAlertCategory Category,
    string Summary,
    string? Detail,
    DateTimeOffset OccurredAt,
    string DedupKey,
    string? AffectedEntityType,
    Guid? AffectedEntityId,
    string? RecommendedAction,
    string? ActionUrl,
    /// <summary>
    /// Follow-up C: an optional count of the underlying affected items (e.g. how many expected
    /// documents were missing) — safe, non-sensitive metadata included in the operations
    /// notification email. Never a filename or content.
    /// </summary>
    int? AffectedItemCount = null,
    /// <summary>
    /// Follow-up F: optional explicit discriminator for the underlying failure. Only
    /// <see cref="AdministrativeAlertReason.MissingDocumentExport"/> queues an operations
    /// notification email; all other report-generation failures leave this null and are recorded
    /// without an email.
    /// </summary>
    AdministrativeAlertReason? Reason = null);
