namespace HR.Modules.DataImport.Features.ConfirmImportSession;

internal sealed record ConfirmImportSessionRowResult(
    int RowNumber,
    Guid EmployeeId,
    string EmployeeNumber);

internal sealed record ConfirmImportSessionResponse(
    Guid ImportSessionId,
    string Status,
    int CreatedCount,
    int FailedCount,
    IReadOnlyList<ConfirmImportSessionRowResult> CreatedRows);
