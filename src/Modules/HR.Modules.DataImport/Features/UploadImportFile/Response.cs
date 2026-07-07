namespace HR.Modules.DataImport.Features.UploadImportFile;

internal sealed record UploadImportFileResponse(
    Guid Id,
    Guid CompanyId,
    string EntityType,
    string FileName,
    string Status,
    int TotalRows,
    DateTimeOffset CreatedAt);
