using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.DeactivateCompanyDocumentCategory;

internal sealed class DeactivateCompanyDocumentCategoryHandler(DocumentsDbContext db, IClock clock)
{
    public async Task<Result> HandleAsync(
        DeactivateCompanyDocumentCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var category = await db.CompanyDocumentCategories
            .SingleOrDefaultAsync(
                c => c.Id == request.CategoryId &&
                     c.CompanyId == request.CompanyId &&
                     c.IsActive,
                cancellationToken);

        if (category is null)
            return Result.Failure(Error.NotFound($"Document category '{request.CategoryId}' was not found."));

        category.Deactivate(clock.UtcNowOffset());
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
