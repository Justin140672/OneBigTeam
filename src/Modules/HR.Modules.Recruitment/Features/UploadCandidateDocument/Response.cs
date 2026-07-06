namespace HR.Modules.Recruitment.Features.UploadCandidateDocument;

internal sealed record UploadCandidateDocumentResponse(
    Guid Id,
    Guid CompanyId,
    Guid CandidateId,
    string Title,
    string FileName,
    long FileSize,
    string ContentType,
    DateTimeOffset CreatedAt);
