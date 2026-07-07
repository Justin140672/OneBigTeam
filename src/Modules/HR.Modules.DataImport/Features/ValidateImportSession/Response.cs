namespace HR.Modules.DataImport.Features.ValidateImportSession;

internal sealed record ValidateImportSessionResponse(
    Guid Id,
    string Status,
    int TotalRows,
    int SuccessfulRows,
    int FailedRows);
