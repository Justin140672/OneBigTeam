namespace HR.Modules.DataImport.Features.GetImportPreview;

internal sealed record ImportPreviewRow(
    int RowNumber,
    string? FirstName,
    string? LastName,
    string? WorkEmail,
    string? DepartmentName,
    string? LocationName,
    string? EmploymentTypeName,
    string? PositionProfileTitle,
    string? ManagerReference,
    string? StartDate);

internal sealed record ImportPreviewRowError(int RowNumber, string Severity, string Message);

internal sealed record GetImportPreviewResponse(
    Guid ImportSessionId,
    string Status,
    int TotalRows,
    int ValidRowCount,
    int InvalidRowCount,
    IReadOnlyList<ImportPreviewRow> ValidRows,
    IReadOnlyList<ImportPreviewRowError> RowErrors,
    IReadOnlyList<ImportPreviewRowError> ReferenceDataCreatedWarnings);
