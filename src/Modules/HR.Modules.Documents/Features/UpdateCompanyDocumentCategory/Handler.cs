using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.UpdateCompanyDocumentCategory;

internal sealed class UpdateCompanyDocumentCategoryHandler(DocumentsDbContext db, IClock clock)
{
    public async Task<Result<UpdateCompanyDocumentCategoryResponse>> HandleAsync(
        UpdateCompanyDocumentCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var category = await db.CompanyDocumentCategories
            .SingleOrDefaultAsync(
                c => c.Id == request.CategoryId &&
                     c.CompanyId == request.CompanyId &&
                     c.IsActive,
                cancellationToken);

        if (category is null)
        {
            return Result.Failure<UpdateCompanyDocumentCategoryResponse>(
                Error.NotFound($"Document category '{request.CategoryId}' was not found."));
        }

        var newName = request.Name.Trim();
        if (!string.Equals(category.Name, newName, StringComparison.Ordinal))
        {
            var nameExists = await db.CompanyDocumentCategories
                .AnyAsync(
                    c => c.CompanyId == request.CompanyId &&
                         c.Id != request.CategoryId &&
                         c.Name == newName &&
                         c.IsActive,
                    cancellationToken);

            if (nameExists)
            {
                return Result.Failure<UpdateCompanyDocumentCategoryResponse>(
                    Error.Conflict($"An active document category named '{newName}' already exists in this company."));
            }
        }

        var now = clock.UtcNowOffset();

        category.Rename(newName, now);

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new UpdateCompanyDocumentCategoryResponse(
            category.Id,
            category.CompanyId,
            category.Name,
            category.IsActive,
            category.UpdatedAt));
    }
}
