namespace HR.Modules.Documents.Features.ListCompanyDocumentCategories;

internal sealed record ListCompanyDocumentCategoriesResponse(IReadOnlyList<CompanyDocumentCategoryListItem> Items);

internal sealed record CompanyDocumentCategoryListItem(
    Guid Id,
    string Name,
    bool IsActive);
