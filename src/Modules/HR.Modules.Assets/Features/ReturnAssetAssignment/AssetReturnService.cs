using HR.Modules.Assets.Domain;
using HR.Modules.Assets.Persistence;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
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
        await ReturnAsync(
            companyId,
            assignmentId,
            expectedEmployeeId: null,
            AssetReturnOutcome.Returned,
            returnedBy,
            notes: null,
            cancellationToken);
    }

    public async Task<AssetReturnResult> ReturnAsync(
        Guid companyId,
        Guid assignmentId,
        Guid? expectedEmployeeId,
        AssetReturnOutcome outcome,
        Guid returnedBy,
        string? notes,
        CancellationToken cancellationToken)
    {
        var assignment = await db.AssetAssignments
            .SingleOrDefaultAsync(
                a => a.Id == assignmentId && a.CompanyId == companyId,
                cancellationToken);

        if (assignment is null)
            return AssetReturnResult.NotFound;

        // OFF-04: an offboarding (or other cross-module) caller supplies the employee it believes
        // owns this assignment — e.g. the employee being offboarded. Rejecting a mismatch here is
        // what prevents a task/checklist item from ever being able to close out an assignment
        // belonging to someone else, without duplicating this check in every caller.
        if (expectedEmployeeId.HasValue && assignment.EmployeeId != expectedEmployeeId.Value)
            return AssetReturnResult.EmployeeMismatch;

        if (!assignment.IsActive)
            return AssetReturnResult.AlreadyReturned;

        var asset = await db.Assets
            .SingleOrDefaultAsync(
                a => a.Id == assignment.AssetId && a.CompanyId == companyId,
                cancellationToken);

        var now = clock.UtcNowOffset();
        var domainOutcome = outcome switch
        {
            AssetReturnOutcome.Lost => AssetAssignmentReturnOutcome.Lost,
            AssetReturnOutcome.Damaged => AssetAssignmentReturnOutcome.Damaged,
            _ => AssetAssignmentReturnOutcome.Returned
        };

        assignment.Return(now, domainOutcome);

        // A clean return frees the asset for reassignment. A lost/damaged outcome leaves it
        // unavailable (UnderRepair) pending HR review/write-off — it must not silently reappear in
        // the "assign asset" pool as if nothing happened.
        if (asset is not null)
        {
            if (outcome == AssetReturnOutcome.Returned)
                asset.MarkAvailable(now);
            else
                asset.MarkUnderRepair(now);
        }

        await db.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(new AssetAssignmentReturnedAuditEvent(
            companyId,
            assignment.Id,
            assignment.AssetId,
            assignment.EmployeeId,
            returnedBy,
            now,
            outcome.ToString(),
            notes), cancellationToken);

        return AssetReturnResult.Success;
    }
}
