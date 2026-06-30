using HR.Modules.Assets.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Features.ReturnAssetAssignment;

internal sealed class AssetReturnService(
    AssetsDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher) : IAssetReturnService
{
    public async Task ReturnAsync(
        Guid companyId,
        Guid assignmentId,
        Guid returnedBy,
        CancellationToken cancellationToken)
    {
        var assignment = await db.AssetAssignments
            .SingleOrDefaultAsync(
                a => a.Id == assignmentId && a.CompanyId == companyId,
                cancellationToken);

        if (assignment is null || !assignment.IsActive)
            return;

        var asset = await db.Assets
            .SingleOrDefaultAsync(
                a => a.Id == assignment.AssetId && a.CompanyId == companyId,
                cancellationToken);

        var now = clock.UtcNowOffset();
        assignment.Return(now);
        asset?.MarkAvailable(now);

        await db.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(new AssetAssignmentReturnedAuditEvent(
            companyId,
            assignment.Id,
            assignment.AssetId,
            assignment.EmployeeId,
            returnedBy,
            now), cancellationToken);
    }
}
