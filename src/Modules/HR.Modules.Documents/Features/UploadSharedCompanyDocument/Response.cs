namespace HR.Modules.Documents.Features.UploadSharedCompanyDocument;

internal sealed record UploadSharedCompanyDocumentResponse(
    Guid Id,
    Guid CompanyId,
    string Title,
    string? Description,
    Guid CategoryId,
    string FileName,
    long FileSize,
    string ContentType,
    int VersionNumber,
    string Status,
    DateOnly? EffectiveDate,
    DateOnly? ReviewDate,
    Guid CreatedBy,
    DateTimeOffset CreatedAt);
