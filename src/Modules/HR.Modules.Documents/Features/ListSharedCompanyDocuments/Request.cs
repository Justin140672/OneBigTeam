using HR.Modules.Documents.Domain;

namespace HR.Modules.Documents.Features.ListSharedCompanyDocuments;

internal sealed record ListSharedCompanyDocumentsRequest
{
    public Guid CompanyId { get; init; }

    public string? Search { get; init; }

    public Guid? CategoryId { get; init; }

    public SharedCompanyDocumentStatus? Status { get; init; }

    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
