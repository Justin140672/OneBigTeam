using HR.Modules.Sickness.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.DeactivateSicknessCategory;

internal sealed class DeactivateSicknessCategoryHandler(SicknessDbContext db, IClock clock, IAuditEventPublisher auditPublisher)
{
    public async Task<Result> HandleAsync(
        DeactivateSicknessCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var category = await db.SicknessCategories
            .FirstOrDefaultAsync(c => c.Id == request.Id && c.CompanyId == request.CompanyId, cancellationToken);

        if (category is null)
            return Result.Failure(Error.NotFound("Sickness category not found."));

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        category.Deactivate(now);

        await db.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(new SicknessCategoryDeactivatedAuditEvent(
            category.CompanyId,
            category.Id,
            request.ActorEmployeeId,
            category.Name,
            now), cancellationToken);

        return Result.Success();
    }
}
