namespace HR.Modules.Leave.Features.GetLeaveRequestAuditHistory;

internal sealed record LeaveAuditFieldChangeItem(string Field, string Before, string After);

internal sealed record LeaveAuditHistoryItem(
    DateTimeOffset OccurredAt,
    string Action,
    string User,
    IReadOnlyList<LeaveAuditFieldChangeItem> Changes);

internal sealed record GetLeaveRequestAuditHistoryResponse(IReadOnlyList<LeaveAuditHistoryItem> Items);
