using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Services;

// Feature-specific writer scoped only to EmployeeTimelineEntry — not a generic repository.
// Enforces the same dedup natural key as the two partial unique indexes on the table (see
// EmployeeTimelineEntryConfiguration) so callers get a query-then-insert fast path with the DB
// constraint as a race-safe backstop.
internal interface IEmployeeTimelineWriter
{
    Task<bool> TryAddAsync(EmployeeTimelineEntry entry, CancellationToken cancellationToken);
}
