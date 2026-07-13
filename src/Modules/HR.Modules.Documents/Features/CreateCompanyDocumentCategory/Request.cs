namespace HR.Modules.Documents.Features.CreateCompanyDocumentCategory;

internal sealed record CreateCompanyDocumentCategoryRequest
{
    public Guid CompanyId { get; init; }
    public string Name { get; init; } = string.Empty;
}
