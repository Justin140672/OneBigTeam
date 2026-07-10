namespace HR.Modules.DataImport.Features.GetImportSession;

internal sealed record GetImportSessionResponse(
    Guid Id,
    string EntityType,
    string FileName,
    string Status,
    int TotalRows,
    int ProcessedRows,
    int SuccessfulRows,
    int FailedRows,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorSummary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
