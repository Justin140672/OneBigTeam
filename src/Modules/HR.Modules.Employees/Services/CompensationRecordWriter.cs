using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed record CompensationWriteResult(Compensation Created, Compensation? ClosedPrevious);

internal sealed class CompensationRecordWriter(EmployeesDbContext dbContext, IClock clock)
{
    public async Task<Result<CompensationWriteResult>> WriteAsync(
        Guid companyId,
        Guid employeeId,
        DateOnly effectiveFrom,
        SalaryType salaryType,
        decimal salary,
        string currency,
        decimal? hoursPerWeek,
        decimal? fte,
        string? notes,
        CompensationChangeReason reason,
        Guid createdBy,
        CancellationToken cancellationToken)
    {
        var employeeExists = await dbContext.Employees
            .AnyAsync(e => e.CompanyId == companyId && e.Id == employeeId, cancellationToken);

        if (!employeeExists)
            return Result.Failure<CompensationWriteResult>(
                Error.NotFound($"Employee '{employeeId}' was not found."));

        var overlapping = await dbContext.Compensations
            .Where(c => c.CompanyId == companyId && c.EmployeeId == employeeId &&
                        effectiveFrom <= (c.EffectiveTo ?? DateOnly.MaxValue))
            .OrderBy(c => c.EffectiveFrom)
            .ToListAsync(cancellationToken);

        Compensation? previous = null;

        if (overlapping.Count > 0)
        {
            var soleOpenRecordStartingBefore =
                overlapping.Count == 1 &&
                overlapping[0].EffectiveTo is null &&
                overlapping[0].EffectiveFrom < effectiveFrom;

            if (!soleOpenRecordStartingBefore)
            {
                var conflict = overlapping[0];
                return Result.Failure<CompensationWriteResult>(
                    Error.Conflict(
                        $"Effective date {effectiveFrom:yyyy-MM-dd} overlaps with an existing compensation record " +
                        $"({conflict.EffectiveFrom:yyyy-MM-dd} to {(conflict.EffectiveTo.HasValue ? conflict.EffectiveTo.Value.ToString("yyyy-MM-dd") : "present")})."));
            }

            previous = overlapping[0];
        }

        var now = clock.UtcNowOffset();

        previous?.Close(effectiveFrom.AddDays(-1), now);

        var record = Compensation.Create(
            Guid.NewGuid(),
            companyId,
            employeeId,
            effectiveFrom,
            salaryType,
            salary,
            currency.Trim().ToUpperInvariant(),
            hoursPerWeek,
            fte,
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            reason,
            createdBy,
            now);

        dbContext.Compensations.Add(record);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new CompensationWriteResult(record, previous));
    }
}
