using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.UpdateLeaveType;

internal sealed class UpdateLeaveTypeHandler(LeaveDbContext db, IClock clock)
{
    public async Task<Result<UpdateLeaveTypeResponse>> HandleAsync(
        UpdateLeaveTypeRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await db.LeaveTypes
            .FirstOrDefaultAsync(t => t.Id == request.Id && t.CompanyId == request.CompanyId, cancellationToken);

        if (entity is null)
            return Result.Failure<UpdateLeaveTypeResponse>(Error.NotFound("Leave type not found."));

        // System leave types (e.g. Annual Leave — see LeaveType.IsSystem) can never be renamed;
        // every other field (code, default entitlement, accrual method, behaviour, tracks-balance)
        // remains editable. Matches item 50's "not renamable" product decision.
        if (entity.IsSystem && !string.Equals(request.Name, entity.Name, StringComparison.Ordinal))
            return Result.Failure<UpdateLeaveTypeResponse>(
                Error.Conflict($"'{entity.Name}' is a system leave type and cannot be renamed."));

        var code = request.Code.ToUpperInvariant();

        var codeConflict = await db.LeaveTypes.AnyAsync(
            t => t.CompanyId == request.CompanyId && t.Code == code && t.Id != request.Id,
            cancellationToken);

        if (codeConflict)
            return Result.Failure<UpdateLeaveTypeResponse>(
                Error.Conflict($"A leave type with code '{code}' already exists."));

        entity.Update(request.Name, code, request.DefaultEntitlementDays,
            request.AccrualMethod, request.Behaviour,
            new DateTimeOffset(clock.UtcNow, TimeSpan.Zero),
            request.HasBalance);

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new UpdateLeaveTypeResponse(
            entity.Id, entity.CompanyId, entity.Name, entity.Code,
            entity.DefaultEntitlementDays,
            entity.AccrualMethod.ToString(),
            entity.Behaviour.ToString(),
            entity.IsActive, entity.HasBalance, entity.IsSystem, entity.UpdatedAt));
    }
}
