using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.CreateSicknessCategory;

internal sealed class CreateSicknessCategoryHandler(SicknessDbContext db, IClock clock, IAuditEventPublisher auditPublisher)
{
    public async Task<Result<CreateSicknessCategoryResponse>> HandleAsync(
        CreateSicknessCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var exists = await db.SicknessCategories
            .AnyAsync(c => c.CompanyId == request.CompanyId && c.Name.ToLower() == request.Name.Trim().ToLower(), cancellationToken);

        if (exists)
            return Result.Failure<CreateSicknessCategoryResponse>(Error.Conflict("A sickness category with this name already exists."));

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        var entity = SicknessCategory.Create(Guid.NewGuid(), request.CompanyId, request.Name, request.DisplayOrder, now);

        db.SicknessCategories.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(new SicknessCategoryCreatedAuditEvent(
            entity.CompanyId,
            entity.Id,
            request.ActorEmployeeId,
            entity.Name,
            entity.DisplayOrder,
            now), cancellationToken);

        return Result.Success(new CreateSicknessCategoryResponse(
            entity.Id, entity.CompanyId, entity.Name, entity.IsActive, entity.DisplayOrder, entity.CreatedAt, entity.UpdatedAt));
    }
}
