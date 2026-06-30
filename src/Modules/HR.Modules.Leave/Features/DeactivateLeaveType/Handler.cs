using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.DeactivateLeaveType;

internal sealed class DeactivateLeaveTypeHandler(LeaveDbContext db, IClock clock)
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

        entity.Deactivate(new DateTimeOffset(clock.UtcNow, TimeSpan.Zero));
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
