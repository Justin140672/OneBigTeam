namespace HR.Modules.Documents.Features.ListPublishedSharedCompanyDocuments;

internal sealed record ListPublishedSharedCompanyDocumentsRequest
{
    public Guid CompanyId { get; init; }
}
