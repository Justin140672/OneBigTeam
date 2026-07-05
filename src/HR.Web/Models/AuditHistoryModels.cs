namespace HR.Web.Models;

public sealed record AuditFieldChangeModel(string Field, string Before, string After);

public sealed record AuditHistoryItemModel(
    DateTimeOffset OccurredAt,
    string Action,
    string Module,
    string User,
    IReadOnlyList<AuditFieldChangeModel> Changes);

public sealed record GetEmployeeAuditHistoryResponse(IReadOnlyList<AuditHistoryItemModel> Items);
