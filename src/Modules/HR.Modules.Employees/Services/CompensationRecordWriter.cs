using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

/// <summary>
/// The outcome of a single compensation write: the newly created record and, if an existing
/// open-ended record had to be closed to make way for it, that closed record (for audit purposes).
/// </summary>
internal sealed record CompensationWriteResult(Compensation Created, Compensation? ClosedPrevious);

/// <summary>
/// Shared compensation-creation domain rule, factored out of CreateCompensationRecordHandler so
/// that BulkApplyCompensationAdjustments and ImportCompensationChanges reuse the exact same
/// overlap/conflict check and "close the previous open record" behaviour rather than duplicating
/// it ad hoc. Callers remain responsible for publishing their own audit events after the write
/// (and, for bulk callers, after their own transaction commits) since the events differ by context.
/// </summary>
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

        // A new record is always created open-ended, i.e. its period is [EffectiveFrom, ∞). It overlaps an
        // existing record R whenever EffectiveFrom <= (R.EffectiveTo ?? MaxValue) — this catches both the
        // normal "close the currently open record" case and backdating into an already-closed historical period.
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
