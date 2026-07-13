namespace HR.Modules.Documents.Features.ListCompanyDocumentCategories;

internal sealed record ListCompanyDocumentCategoriesRequest
{
    public Guid CompanyId { get; init; }
    public bool IncludeInactive { get; init; } = false;
}
