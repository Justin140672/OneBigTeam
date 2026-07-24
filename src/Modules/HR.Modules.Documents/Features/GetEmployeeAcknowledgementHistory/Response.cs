namespace HR.Modules.Documents.Features.GetEmployeeAcknowledgementHistory;

internal sealed record GetEmployeeAcknowledgementHistoryItem(
    string DocumentTitle,
    int VersionNumber,
    DateTimeOffset AcknowledgedAt);

internal sealed record GetEmployeeAcknowledgementHistoryResponse(
    IReadOnlyList<GetEmployeeAcknowledgementHistoryItem> Items);
