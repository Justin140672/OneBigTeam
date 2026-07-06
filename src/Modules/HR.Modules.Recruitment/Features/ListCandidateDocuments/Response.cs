namespace HR.Modules.Recruitment.Features.ListCandidateDocuments;

internal sealed record ListCandidateDocumentsResponse(IReadOnlyList<CandidateDocumentListItem> Items);

internal sealed record CandidateDocumentListItem(
    Guid Id,
    string Title,
    string FileName,
    long FileSize,
    string ContentType,
    DateTimeOffset CreatedAt);
