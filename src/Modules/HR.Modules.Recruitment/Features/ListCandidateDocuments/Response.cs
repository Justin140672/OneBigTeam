namespace HR.Modules.Recruitment.Features.ListCandidateDocuments;

internal sealed record ListCandidateDocumentsResponse(IReadOnlyList<CandidateDocumentListItem> Items);

internal sealed record CandidateDocumentListItem(
    Guid Id,
    string Title,
    string Kind,
    string FileName,
    long FileSize,
    string ContentType,
    DateTimeOffset CreatedAt,
    // Internal recruitment Ticket 2: true only for the newest Kind = Cv document.
    bool IsCurrentCv = false,
    // Internal recruitment Ticket 2: number of applications that reference this document as their submitted CV.
    int ReferencingApplicationCount = 0,
    // [P1] Malware scan state: Pending | Scanning | Clean | Infected | Failed. Only Clean documents
    // are downloadable (IsDownloadable); the download endpoint enforces this server-side regardless.
    string ScanStatus = "Pending",
    bool IsDownloadable = false);
