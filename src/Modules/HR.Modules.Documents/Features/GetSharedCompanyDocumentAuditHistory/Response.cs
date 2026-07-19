namespace HR.Modules.Documents.Features.GetSharedCompanyDocumentAuditHistory;

internal sealed record AuditFieldChangeItem(string Field, string Before, string After);

internal sealed record AuditHistoryItem(
    DateTimeOffset OccurredAt,
    string Action,
    string User,
    IReadOnlyList<AuditFieldChangeItem> Changes);

internal sealed record GetSharedCompanyDocumentAuditHistoryResponse(IReadOnlyList<AuditHistoryItem> Items);
