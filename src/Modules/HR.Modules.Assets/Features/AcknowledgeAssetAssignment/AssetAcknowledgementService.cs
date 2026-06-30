using HR.Modules.Assets.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Features.AcknowledgeAssetAssignment;

internal sealed class AssetAcknowledgementService(
    AssetsDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher) : IAssetAcknowledgementService
{
    public async Task AcknowledgeAsync(
        Guid companyId,
        Guid assignmentId,
        Guid acknowledgedBy,
        CancellationToken cancellationToken)
    {
        var assignment = await db.AssetAssignments
            .SingleOrDefaultAsync(
                a => a.Id == assignmentId && a.CompanyId == companyId,
                cancellationToken);

        if (assignment is null)
            return;

        if (assignment.AcknowledgedAt is not null)
            return;

        var now = clock.UtcNowOffset();
        assignment.Acknowledge(now);

        await db.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(new AssetAssignmentAcknowledgedAuditEvent(
            companyId,
            assignment.Id,
            assignment.AssetId,
            assignment.EmployeeId,
            acknowledgedBy,
            now), cancellationToken);
    }
}
