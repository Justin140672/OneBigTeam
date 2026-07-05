namespace HR.Modules.Employees.Features.GetEmployeeAuditHistory;

internal sealed record AuditFieldChangeItem(string Field, string Before, string After);

internal sealed record AuditHistoryItem(
    DateTimeOffset OccurredAt,
    string Action,
    string Module,
    string User,
    IReadOnlyList<AuditFieldChangeItem> Changes);

internal sealed record GetEmployeeAuditHistoryResponse(IReadOnlyList<AuditHistoryItem> Items);
