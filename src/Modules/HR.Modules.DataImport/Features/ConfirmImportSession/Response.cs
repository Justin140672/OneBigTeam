namespace HR.Modules.DataImport.Features.ConfirmImportSession;

/// <summary>
/// A single successfully-created row from the import, carrying the employee number that was
/// actually assigned (the auto-generated number in Automatic mode, or the file-supplied value in
/// Manual mode) — not simply an echo of whatever was in the original staged file, which would be
/// blank for Automatic-mode rows.
/// </summary>
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
