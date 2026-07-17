namespace HR.Modules.Documents.Features.ExpireSharedCompanyDocument;

internal sealed record ExpireSharedCompanyDocumentResponse(
    Guid Id,
    Guid CompanyId,
    string Status,
    Guid ExpiredBy,
    DateTimeOffset ExpiredAt,
    int ReviewTasksCancelled);
