namespace HR.Modules.DataImport.Features.ListImportSessions;

internal sealed record ImportSessionSummary(
    Guid Id,
    string FileName,
    string Status,
    int TotalRows,
    int SuccessfulRows,
    int FailedRows,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);
