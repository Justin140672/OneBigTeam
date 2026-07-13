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
    IReadOnlyList<Guid> AudienceDepartmentIds,
    IReadOnlyList<Guid> AudienceLocationIds,
    IReadOnlyList<Guid> AudiencePositionProfileIds,
    IReadOnlyList<Guid> AudienceEmployeeIds,
    bool RequiresAcknowledgement,
    Guid CreatedBy,
    DateTimeOffset CreatedAt);
