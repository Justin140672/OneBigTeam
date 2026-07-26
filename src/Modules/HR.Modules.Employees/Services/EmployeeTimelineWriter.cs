using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class EmployeeTimelineWriter(EmployeesDbContext dbContext) : IEmployeeTimelineWriter
{
    // Query-then-insert, race-safe against the DB's two partial unique indexes (see
    // EmployeeTimelineEntryConfiguration) as a backstop — if a concurrent writer beats us to it,
    // the DB rejects the insert and we swallow the constraint violation rather than letting it
    // propagate, returning false either way.
    public async Task<bool> TryAddAsync(EmployeeTimelineEntry entry, CancellationToken cancellationToken)
    {
        var alreadyExists = entry.SourceRecordId is not null
            ? await dbContext.EmployeeTimelineEntries.AnyAsync(
                e => e.CompanyId == entry.CompanyId
                    && e.SourceModule == entry.SourceModule
                    && e.EventType == entry.EventType
                    && e.SourceRecordId == entry.SourceRecordId,
                cancellationToken)
            : await dbContext.EmployeeTimelineEntries.AnyAsync(
                e => e.CompanyId == entry.CompanyId
                    && e.EmployeeId == entry.EmployeeId
                    && e.EventType == entry.EventType
                    && e.EventDate == entry.EventDate
                    && e.SourceRecordId == null,
                cancellationToken);

        if (alreadyExists)
            return false;

        dbContext.EmployeeTimelineEntries.Add(entry);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Backstop for the race where a concurrent writer inserted a matching entry between
            // our existence check and this SaveChangesAsync — the DB's partial unique indexes
            // reject the duplicate, and we treat that the same as "not added" rather than
            // propagating the failure.
            dbContext.Entry(entry).State = EntityState.Detached;
            return false;
        }
    }
}
