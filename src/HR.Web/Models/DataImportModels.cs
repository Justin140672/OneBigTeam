namespace HR.Web.Models;

public sealed record UploadImportFileResponse(
    Guid Id,
    Guid CompanyId,
    string EntityType,
    string FileName,
    string Status,
    int TotalRows,
    DateTimeOffset CreatedAt);

public sealed record ValidateImportSessionResponse(
    Guid Id,
    string Status,
    int TotalRows,
    int SuccessfulRows,
    int FailedRows);

public sealed record ImportPreviewRow(
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

public sealed record ImportPreviewRowError(int RowNumber, string Severity, string Message);

public sealed record GetImportPreviewResponse(
    Guid ImportSessionId,
    string Status,
    int TotalRows,
    int ValidRowCount,
    int InvalidRowCount,
    IReadOnlyList<ImportPreviewRow> ValidRows,
    IReadOnlyList<ImportPreviewRowError> RowErrors,
    IReadOnlyList<ImportPreviewRowError> ReferenceDataCreatedWarnings);

public sealed record ConfirmImportSessionResponse(
    Guid ImportSessionId,
    string Status,
    int CreatedCount,
    int FailedCount);

public sealed record ImportFieldSuggestion(
    string TargetField,
    string StandardHeaderName,
    string? SuggestedHeader);

public sealed record GetImportSessionColumnsResponse(
    Guid ImportSessionId,
    IReadOnlyList<string> DetectedHeaders,
    IReadOnlyList<ImportFieldSuggestion> FieldSuggestions);

public sealed record ImportSessionSummary(
    Guid Id,
    string FileName,
    string Status,
    int TotalRows,
    int SuccessfulRows,
    int FailedRows,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

public sealed record GetImportSessionResponse(
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
