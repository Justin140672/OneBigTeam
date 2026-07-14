namespace HR.Modules.Documents.Features.ArchiveSharedCompanyDocument;

internal sealed record ArchiveSharedCompanyDocumentResponse(
    Guid Id,
    Guid CompanyId,
    string Status,
    Guid ArchivedBy,
    DateTimeOffset ArchivedAt,
    string ArchiveReason,
    int AcknowledgementTasksCancelled);
