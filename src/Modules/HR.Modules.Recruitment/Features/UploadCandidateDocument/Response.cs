namespace HR.Modules.Recruitment.Features.UploadCandidateDocument;

internal sealed record UploadCandidateDocumentResponse(
    Guid Id,
    Guid CompanyId,
    Guid CandidateId,
    string Title,
    string Kind,
    string FileName,
    long FileSize,
    string ContentType,
    DateTimeOffset CreatedAt,
    // [P1] Always "Pending" for a fresh upload — the document is not downloadable until scanned Clean.
    string ScanStatus = "Pending");
