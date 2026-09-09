using HR.Modules.Sickness.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.UpdateSicknessCategory;

internal sealed class UpdateSicknessCategoryHandler(SicknessDbContext db, IClock clock, IAuditEventPublisher auditPublisher)
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

        var beforeName = category.Name;
        var beforeDisplayOrder = category.DisplayOrder;
        var beforeIsActive = category.IsActive;

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        category.Update(request.Name, request.DisplayOrder, category.IsActive, now);

        // Ticket 2: optimistic concurrency (base-code helper). Nothing commits on conflict, so the
        // audit event below only runs on a successful save.
        var saveResult = await db.SaveChangesWithConcurrencyAsync(
            category,
            request.ExpectedVersion,
            "This sickness category was changed by someone else since you opened it. Reload the latest details and try again.",
            cancellationToken);

        if (saveResult.IsFailure)
            return Result.Failure<UpdateSicknessCategoryResponse>(saveResult.Error);

        await auditPublisher.PublishAsync(new SicknessCategoryUpdatedAuditEvent(
            category.CompanyId,
            category.Id,
            request.ActorEmployeeId,
            beforeName,
            beforeDisplayOrder,
            beforeIsActive,
            category.Name,
            category.DisplayOrder,
            category.IsActive,
            now), cancellationToken);

        return Result.Success(new UpdateSicknessCategoryResponse(
            category.Id, category.CompanyId, category.Name, category.IsActive, category.DisplayOrder, category.CreatedAt, category.UpdatedAt, category.Version));
    }
}
