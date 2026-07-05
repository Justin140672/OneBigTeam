using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.CreateCompensationRecord;

internal sealed class CreateCompensationRecordHandler(
    EmployeesDbContext dbContext,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<CreateCompensationRecordResponse>> HandleAsync(
        CreateCompensationRecordRequest request,
        CancellationToken cancellationToken)
    {
        var employeeExists = await dbContext.Employees
            .AnyAsync(e => e.CompanyId == request.CompanyId && e.Id == request.EmployeeId, cancellationToken);

        if (!employeeExists)
            return Result.Failure<CreateCompensationRecordResponse>(
                Error.NotFound($"Employee '{request.EmployeeId}' was not found."));

        // A new record is always created open-ended, i.e. its period is [EffectiveFrom, ∞). It overlaps an
        // existing record R whenever EffectiveFrom <= (R.EffectiveTo ?? MaxValue) — this catches both the
        // normal "close the currently open record" case and backdating into an already-closed historical period.
        var overlapping = await dbContext.Compensations
            .Where(c => c.CompanyId == request.CompanyId && c.EmployeeId == request.EmployeeId &&
                        request.EffectiveFrom <= (c.EffectiveTo ?? DateOnly.MaxValue))
            .OrderBy(c => c.EffectiveFrom)
            .ToListAsync(cancellationToken);

        Compensation? previous = null;

        if (overlapping.Count > 0)
        {
            var soleOpenRecordStartingBefore =
                overlapping.Count == 1 &&
                overlapping[0].EffectiveTo is null &&
                overlapping[0].EffectiveFrom < request.EffectiveFrom;

            if (!soleOpenRecordStartingBefore)
            {
                var conflict = overlapping[0];
                return Result.Failure<CreateCompensationRecordResponse>(
                    Error.Conflict(
                        $"Effective date {request.EffectiveFrom:yyyy-MM-dd} overlaps with an existing compensation record " +
                        $"({conflict.EffectiveFrom:yyyy-MM-dd} to {(conflict.EffectiveTo.HasValue ? conflict.EffectiveTo.Value.ToString("yyyy-MM-dd") : "present")})."));
            }

            previous = overlapping[0];
        }

        var now = clock.UtcNowOffset();

        if (previous is not null)
        {
            previous.Close(request.EffectiveFrom.AddDays(-1), now);

            await auditEventPublisher.PublishAsync(
                new CompensationRecordClosedAuditEvent(
                    request.CompanyId, request.EmployeeId, previous.Id, previous.EffectiveFrom, previous.EffectiveTo!.Value, now),
                cancellationToken);
        }

        var record = Compensation.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.EmployeeId,
            request.EffectiveFrom,
            request.SalaryType,
            request.Salary,
            request.Currency.Trim().ToUpperInvariant(),
            request.HoursPerWeek,
            request.FTE,
            string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            now);

        dbContext.Compensations.Add(record);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new CompensationRecordCreatedAuditEvent(
                request.CompanyId, request.EmployeeId, record.Id, record.EffectiveFrom,
                record.SalaryType.ToString(), record.Salary, record.Currency, now),
            cancellationToken);

        return Result.Success(new CreateCompensationRecordResponse(
            record.Id,
            record.CompanyId,
            record.EmployeeId,
            record.EffectiveFrom,
            record.EffectiveTo,
            record.SalaryType.ToString(),
            record.Salary,
            record.Currency,
            record.HoursPerWeek,
            record.FTE,
            record.Notes,
            record.CreatedAt,
            record.UpdatedAt));
    }
}
