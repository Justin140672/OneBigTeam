namespace HR.Modules.Documents.Features.ListSharedCompanyDocuments;

internal sealed record ListSharedCompanyDocumentsRequest
{
    public Guid CompanyId { get; init; }
}
