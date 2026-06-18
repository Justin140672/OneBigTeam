namespace HR.Modules.Documents.Features.GetExpiringDocuments;

internal sealed record GetExpiringDocumentsRequest
{
    public Guid CompanyId { get; init; }
}
