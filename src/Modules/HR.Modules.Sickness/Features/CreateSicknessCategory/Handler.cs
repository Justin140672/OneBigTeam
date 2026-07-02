using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.CreateSicknessCategory;

internal sealed class CreateSicknessCategoryHandler(SicknessDbContext db, IClock clock)
{
    public async Task<Result<CreateSicknessCategoryResponse>> HandleAsync(
        CreateSicknessCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var exists = await db.SicknessCategories
            .AnyAsync(c => c.CompanyId == request.CompanyId && c.Name == request.Name, cancellationToken);

        if (exists)
            return Result.Failure<CreateSicknessCategoryResponse>(Error.Conflict("A sickness category with this name already exists."));

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        var entity = SicknessCategory.Create(Guid.NewGuid(), request.CompanyId, request.Name, request.DisplayOrder, now);

        db.SicknessCategories.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateSicknessCategoryResponse(
            entity.Id, entity.CompanyId, entity.Name, entity.IsActive, entity.DisplayOrder, entity.CreatedAt, entity.UpdatedAt));
    }
}
