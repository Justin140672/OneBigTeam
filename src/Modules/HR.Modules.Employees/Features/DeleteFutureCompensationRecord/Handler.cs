using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.DeleteFutureCompensationRecord;

internal sealed class DeleteFutureCompensationRecordHandler(
    EmployeesDbContext dbContext,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result> HandleAsync(
        Guid companyId,
        Guid employeeId,
        Guid id,
        CancellationToken cancellationToken)
    {
        var record = await dbContext.Compensations
            .SingleOrDefaultAsync(
                c => c.Id == id && c.CompanyId == companyId && c.EmployeeId == employeeId,
                cancellationToken);

        if (record is null)
            return Result.Failure(Error.NotFound($"Compensation record '{id}' was not found."));

        var today = DateOnly.FromDateTime(clock.UtcNow);

        if (record.EffectiveFrom <= today)
            return Result.Failure(Error.Conflict("Only future-dated compensation records can be deleted."));

        var hasLaterRecord = await dbContext.Compensations
            .AnyAsync(
                c => c.CompanyId == companyId && c.EmployeeId == employeeId &&
                     c.Id != record.Id && c.EffectiveFrom > record.EffectiveFrom,
                cancellationToken);

        if (hasLaterRecord)
            return Result.Failure(
                Error.Conflict("A later compensation record exists for this employee; delete it first."));

        // If this record's creation closed an earlier one, reopen that predecessor so the
        // employee's compensation timeline has no gap once this future record is removed.
        var predecessor = await dbContext.Compensations
            .SingleOrDefaultAsync(
                c => c.CompanyId == companyId && c.EmployeeId == employeeId &&
                     c.EffectiveTo == record.EffectiveFrom.AddDays(-1),
                cancellationToken);

        var now = clock.UtcNowOffset();
        predecessor?.Reopen(now);

        dbContext.Compensations.Remove(record);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new CompensationRecordDeletedAuditEvent(companyId, employeeId, record.Id, record.EffectiveFrom, now),
            cancellationToken);

        return Result.Success();
    }
}
