using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.CreateCompanyDocumentCategory;

internal sealed class CreateCompanyDocumentCategoryHandler(DocumentsDbContext db, IClock clock)
{
    public async Task<Result<CreateCompanyDocumentCategoryResponse>> HandleAsync(
        CreateCompanyDocumentCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();

        var nameExists = await db.CompanyDocumentCategories
            .AnyAsync(
                c => c.CompanyId == request.CompanyId &&
                     c.Name == name &&
                     c.IsActive,
                cancellationToken);

        if (nameExists)
        {
            return Result.Failure<CreateCompanyDocumentCategoryResponse>(
                Error.Conflict($"An active document category named '{name}' already exists in this company."));
        }

        var now = clock.UtcNowOffset();

        var category = CompanyDocumentCategory.Create(Guid.NewGuid(), request.CompanyId, name, now);

        db.CompanyDocumentCategories.Add(category);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateCompanyDocumentCategoryResponse(
            category.Id,
            category.CompanyId,
            category.Name,
            category.IsActive,
            category.CreatedAt));
    }
}
