using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.DeleteFutureCompensationRecord;

internal sealed class DeleteFutureCompensationRecordHandler(
    EmployeesDbContext dbContext,
    IClock clock,
    ICompanyTimeZoneReader timeZoneReader,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result> HandleAsync(
        Guid companyId,
        Guid employeeId,
        Guid id,
        Guid actorEmployeeId,
        CancellationToken cancellationToken)
    {
        var record = await dbContext.Compensations
            .SingleOrDefaultAsync(
                c => c.Id == id && c.CompanyId == companyId && c.EmployeeId == employeeId,
                cancellationToken);

        if (record is null)
            return Result.Failure(Error.NotFound($"Compensation record '{id}' was not found."));

        var today = await CompanyToday.ResolveAsync(companyId, clock, timeZoneReader, cancellationToken);

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
        var reopenedPredecessorEffectiveTo = predecessor?.EffectiveTo;
        predecessor?.Reopen(now);

        dbContext.Compensations.Remove(record);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (predecessor is not null && reopenedPredecessorEffectiveTo is not null)
        {
            await auditEventPublisher.PublishAsync(
                new CompensationRecordReopenedAuditEvent(
                    companyId, employeeId, predecessor.Id, actorEmployeeId, predecessor.EffectiveFrom,
                    reopenedPredecessorEffectiveTo.Value, now),
                cancellationToken);
        }

        await auditEventPublisher.PublishAsync(
            new CompensationRecordDeletedAuditEvent(
                companyId, employeeId, record.Id, actorEmployeeId, record.EffectiveFrom, now),
            cancellationToken);

        return Result.Success();
    }
}
