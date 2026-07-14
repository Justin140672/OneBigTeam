namespace HR.Modules.Documents.Features.UploadSharedCompanyDocumentVersion;

internal sealed record UploadSharedCompanyDocumentVersionResponse(
    Guid Id,
    Guid CompanyId,
    int VersionNumber,
    string FileName,
    long FileSize,
    string VersionNote,
    bool RequiresReacknowledgement,
    Guid UploadedBy,
    DateTimeOffset UploadedAt);
