namespace HR.Modules.Documents.Features.DeactivateCompanyDocumentCategory;

internal sealed record DeactivateCompanyDocumentCategoryRequest
{
    public Guid CompanyId { get; init; }
    public Guid CategoryId { get; init; }
}
