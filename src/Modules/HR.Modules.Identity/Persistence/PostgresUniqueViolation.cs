using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Persistence;

/// <summary>
/// Recognises a PostgreSQL unique-constraint violation (SQLSTATE 23505) surfaced through EF Core as
/// a <see cref="DbUpdateException"/>.
///
/// Ticket 24 (P1): module-local copy of the identical helper already used by
/// HR.Modules.Tasks.Persistence.PostgresUniqueViolation and HR.Modules.Notifications.Persistence.
/// Modules must not reference each other's implementation projects (see
/// specifications/architecture/02-module-boundaries.md), so this small, dependency-free helper is
/// duplicated here rather than shared — it is not business logic, just Npgsql exception shape
/// recognition, and is intentionally kept trivial enough that duplication is cheaper than adding a
/// cross-module or SharedKernel dependency for it.
/// </summary>
internal static class PostgresUniqueViolation
{
    private const string UniqueViolationSqlState = "23505";

    public static bool Is(DbUpdateException exception)
    {
        var inner = exception.InnerException;

        return (inner is not null
                && inner.GetType().Name == "PostgresException"
                && string.Equals(
                    inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string,
                    UniqueViolationSqlState,
                    StringComparison.Ordinal))
            || inner?.Message.Contains("duplicate key value violates unique constraint", StringComparison.OrdinalIgnoreCase) == true;
    }
}
