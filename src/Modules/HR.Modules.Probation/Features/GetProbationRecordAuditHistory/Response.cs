namespace HR.Modules.Probation.Features.GetProbationRecordAuditHistory;

internal sealed record ProbationAuditFieldChangeItem(string Field, string Before, string After);

internal sealed record ProbationAuditHistoryItem(
    DateTimeOffset OccurredAt,
    string Action,
    string User,
    IReadOnlyList<ProbationAuditFieldChangeItem> Changes);

internal sealed record GetProbationRecordAuditHistoryResponse(IReadOnlyList<ProbationAuditHistoryItem> Items);
