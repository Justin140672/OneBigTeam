namespace HR.Modules.Documents.Features.PublishSharedCompanyDocument;

internal sealed record PublishSharedCompanyDocumentResponse(
    Guid Id,
    Guid CompanyId,
    string Title,
    string Status,
    Guid PublishedBy,
    DateTimeOffset PublishedAt,
    int AcknowledgementTasksCreated);
