namespace HR.Modules.Identity.Features.GetUserAuditHistory;

internal sealed record UserAuditHistoryItem(
    DateTimeOffset OccurredAt,
    string EventType,
    string Summary,
    string PerformedBy);

internal sealed record GetUserAuditHistoryResponse(IReadOnlyList<UserAuditHistoryItem> Items);
