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
    string? ActionUrl);
