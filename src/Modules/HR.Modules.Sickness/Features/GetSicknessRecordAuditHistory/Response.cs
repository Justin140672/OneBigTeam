namespace HR.Modules.Sickness.Features.GetSicknessRecordAuditHistory;

internal sealed record SicknessAuditFieldChangeItem(string Field, string Before, string After);

internal sealed record SicknessAuditHistoryItem(
    DateTimeOffset OccurredAt,
    string Action,
    string User,
    IReadOnlyList<SicknessAuditFieldChangeItem> Changes);

internal sealed record GetSicknessRecordAuditHistoryResponse(IReadOnlyList<SicknessAuditHistoryItem> Items);
