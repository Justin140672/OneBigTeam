using HR.Modules.Documents.Domain;

namespace HR.Modules.Documents.Features.ListSharedCompanyDocuments;

internal sealed record ListSharedCompanyDocumentsRequest
{
    public Guid CompanyId { get; init; }
    public SharedCompanyDocumentStatus? Status { get; init; }
    public Guid? CategoryId { get; init; }
    public DateOnly? ReviewDateFrom { get; init; }
    public DateOnly? ReviewDateTo { get; init; }
    public string? Search { get; init; }
}
