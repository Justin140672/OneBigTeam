namespace HR.Modules.Notifications.Features.ListOperationalAlerts;

internal sealed record ListOperationalAlertsResponse(
    IReadOnlyList<OperationalAlertListItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

internal sealed record OperationalAlertListItemDto(
    Guid Id,
    Guid CompanyId,
    string Category,
    string Severity,
    string Status,
    string Summary,
    int OccurrenceCount,
    DateTimeOffset FirstOccurredAt,
    DateTimeOffset LastOccurredAt,
    string? AffectedEntityType,
    Guid? AffectedEntityId,
    int? AffectedItemCount,
    DateTimeOffset? ResolvedAt,
    Guid? ResolvedByUserId,
    bool IsRead);
