using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.CreateLeaveType;

internal sealed class CreateLeaveTypeHandler(LeaveDbContext db, IClock clock, IAuditEventPublisher auditPublisher)
{
    public async Task<Result<CreateLeaveTypeResponse>> HandleAsync(
        CreateLeaveTypeRequest request,
        CancellationToken cancellationToken)
    {
        var code = request.Code.ToUpperInvariant();

        var exists = await db.LeaveTypes.AnyAsync(
            t => t.CompanyId == request.CompanyId && t.Code == code,
            cancellationToken);

        if (exists)
            return Result.Failure<CreateLeaveTypeResponse>(
                Error.Conflict($"A leave type with code '{code}' already exists."));

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        var entity = LeaveType.Create(
            Guid.NewGuid(), request.CompanyId, request.Name, code,
            request.DefaultEntitlementDays, request.AccrualMethod, request.Behaviour, now,
            request.HasBalance,
            toilExpiryDays: request.ToilExpiryDays,
            allowNegativeToilBalance: request.AllowNegativeToilBalance);

        db.LeaveTypes.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(new LeaveTypeCreatedAuditEvent(
            entity.CompanyId,
            entity.Id,
            entity.Name,
            entity.Code,
            entity.DefaultEntitlementDays,
            entity.AccrualMethod.ToString(),
            entity.Behaviour.ToString(),
            request.ActorEmployeeId,
            now), cancellationToken);

        return Result.Success(new CreateLeaveTypeResponse(
            entity.Id, entity.CompanyId, entity.Name, entity.Code,
            entity.DefaultEntitlementDays,
            entity.AccrualMethod.ToString(),
            entity.Behaviour.ToString(),
            entity.IsActive, entity.HasBalance, entity.IsSystem,
            entity.ToilExpiryDays, entity.AllowNegativeToilBalance,
            entity.CreatedAt, entity.UpdatedAt));
    }
}
