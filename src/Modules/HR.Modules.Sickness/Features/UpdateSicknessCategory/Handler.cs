using HR.Modules.Sickness.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.UpdateSicknessCategory;

internal sealed class UpdateSicknessCategoryHandler(SicknessDbContext db, IClock clock)
{
    public async Task<Result<UpdateSicknessCategoryResponse>> HandleAsync(
        UpdateSicknessCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var category = await db.SicknessCategories
            .FirstOrDefaultAsync(c => c.Id == request.Id && c.CompanyId == request.CompanyId, cancellationToken);

        if (category is null)
            return Result.Failure<UpdateSicknessCategoryResponse>(Error.NotFound("Sickness category not found."));

        var nameConflict = await db.SicknessCategories
            .AnyAsync(c => c.CompanyId == request.CompanyId && c.Name.ToLower() == request.Name.Trim().ToLower() && c.Id != request.Id, cancellationToken);

        if (nameConflict)
            return Result.Failure<UpdateSicknessCategoryResponse>(Error.Conflict("A sickness category with this name already exists."));

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        category.Update(request.Name, request.DisplayOrder, category.IsActive, now);

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new UpdateSicknessCategoryResponse(
            category.Id, category.CompanyId, category.Name, category.IsActive, category.DisplayOrder, category.CreatedAt, category.UpdatedAt));
    }
}
