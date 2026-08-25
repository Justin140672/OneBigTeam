namespace HR.Modules.Documents.Features.PurgeEligibleArchivedEmployeeDocuments;

internal sealed record PurgeEligibleArchivedEmployeeDocumentsRequest
{
    public Guid CompanyId { get; init; }
}
