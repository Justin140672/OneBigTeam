namespace HR.Modules.Notifications.Features.GetAdministrativeAlerts;

internal sealed record AdministrativeAlertItem(
    Guid Id,
    string Severity,
    string Category,
    string Summary,
    string? Detail,
    int OccurrenceCount,
    DateTimeOffset FirstOccurredAt,
    DateTimeOffset LastOccurredAt,
    string? AffectedEntityType,
    Guid? AffectedEntityId,
    string? RecommendedAction,
    string? ActionUrl,
    bool IsRead,
    string Status,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? ResolvedAt,
    string? ResolutionNote);

internal sealed record GetAdministrativeAlertsResponse(
    int UnreadCount,
    IReadOnlyList<AdministrativeAlertItem> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages);
