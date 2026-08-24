using HR.Modules.Leave.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.DeactivateLeaveType;

internal sealed class DeactivateLeaveTypeHandler(
    LeaveDbContext db,
    IClock clock,
    ICurrentEmployeeReader currentEmployeeReader,
    IAuditEventPublisher auditPublisher)
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

        // System leave types (e.g. Annual Leave, provisioned for every company — see
        // LeaveType.IsSystem) can never be deactivated, matching the "not deletable" product
        // decision for item 50.
        if (entity.IsSystem)
            return Result.Failure(Error.Conflict($"'{entity.Name}' is a system leave type and cannot be deactivated."));

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

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        entity.Deactivate(now);
        await db.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(new LeaveTypeDeactivatedAuditEvent(
            entity.CompanyId,
            entity.Id,
            entity.Name,
            request.ActorEmployeeId,
            now), cancellationToken);

        return Result.Success();
    }
}
