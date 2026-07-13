namespace HR.Modules.Documents.Features.UpdateCompanyDocumentCategory;

internal sealed record UpdateCompanyDocumentCategoryResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    bool IsActive,
    DateTimeOffset UpdatedAt);
