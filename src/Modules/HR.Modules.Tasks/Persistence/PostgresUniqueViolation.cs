using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Persistence;

/// <summary>
/// Recognises a PostgreSQL unique-constraint violation (SQLSTATE 23505) surfaced through EF Core as
/// a <see cref="DbUpdateException"/>, optionally narrowed to a named index so unrelated unique
/// violations are never mistaken for the one a caller expects.
///
/// OBT-REM-13: module-local copy of the identical helper in HR.Modules.Notifications.Persistence.
/// Modules must not reference each other's implementation projects (see
/// specifications/architecture/02-module-boundaries.md), so this small, dependency-free helper is
/// duplicated here rather than shared — it is not business logic, just Npgsql exception shape
/// recognition, and is intentionally kept trivial enough that duplication is cheaper than adding a
/// cross-module or SharedKernel dependency for it.
/// </summary>
internal static class PostgresUniqueViolation
{
    private const string UniqueViolationSqlState = "23505";

    public static bool Is(DbUpdateException exception, string? constraintName = null)
    {
        // Npgsql surfaces the underlying PostgresException as the inner exception. Match on the
        // stable SQLSTATE rather than a localised message where possible.
        var inner = exception.InnerException;
        var isUniqueViolation =
            (inner is not null
                && inner.GetType().Name == "PostgresException"
                && string.Equals(
                    inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string,
                    UniqueViolationSqlState,
                    StringComparison.Ordinal))
            || inner?.Message.Contains("duplicate key value violates unique constraint", StringComparison.OrdinalIgnoreCase) == true;

        if (!isUniqueViolation)
            return false;

        if (constraintName is null)
            return true;

        return inner?.Message.Contains(constraintName, StringComparison.OrdinalIgnoreCase) == true
            || (inner?.GetType().GetProperty("ConstraintName")?.GetValue(inner) as string) == constraintName;
    }
}
