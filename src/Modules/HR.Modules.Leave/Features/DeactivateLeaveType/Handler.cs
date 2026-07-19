using HR.Modules.Leave.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.DeactivateLeaveType;

internal sealed class DeactivateLeaveTypeHandler(
    LeaveDbContext db,
    IClock clock,
    ICurrentEmployeeReader currentEmployeeReader)
{
    public async Task<Result> HandleAsync(
        DeactivateLeaveTypeRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await db.LeaveTypes
            .FirstOrDefaultAsync(t => t.Id == request.Id && t.CompanyId == request.CompanyId, cancellationToken);

        if (entity is null)
            return Result.Failure(Error.NotFound("Leave type not found."));

        if (!entity.IsActive)
            return Result.Failure(Error.Conflict("Leave type is already inactive."));

        // Every employee auto-gets a LeaveBalance row on policy assignment (see
        // AssignLeavePolicyToEmployeeHandler), so an unfiltered count of LeaveBalances would make
        // every leave type permanently undeactivatable. Filter to current (non-terminated)
        // employees only, matching the "in use" definition used for the other 8 deactivatable
        // lookups.
        var currentEmployeeIds = await currentEmployeeReader
            .GetCurrentEmployeeIdsAsync(request.CompanyId, cancellationToken);

        var currentEmployeeBalanceCount = currentEmployeeIds.Count == 0
            ? 0
            : await db.LeaveBalances
                .CountAsync(
                    b => b.LeaveTypeId == request.Id
                      && b.CompanyId == request.CompanyId
                      && currentEmployeeIds.Contains(b.EmployeeId),
                    cancellationToken);

        if (currentEmployeeBalanceCount > 0)
        {
            return Result.Failure(Error.Conflict(
                $"Cannot deactivate '{entity.Name}' — it is currently assigned to " +
                $"{currentEmployeeBalanceCount} active employee{(currentEmployeeBalanceCount == 1 ? "" : "s")}."));
        }

        entity.Deactivate(new DateTimeOffset(clock.UtcNow, TimeSpan.Zero));
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
