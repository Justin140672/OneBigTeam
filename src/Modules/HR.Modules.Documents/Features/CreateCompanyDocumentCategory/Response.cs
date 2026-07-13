namespace HR.Modules.Documents.Features.CreateCompanyDocumentCategory;

internal sealed record CreateCompanyDocumentCategoryResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    bool IsActive,
    DateTimeOffset CreatedAt);
