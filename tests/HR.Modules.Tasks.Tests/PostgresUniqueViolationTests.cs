using HR.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Tests;

/// <summary>
/// OBT-REM-13: <see cref="PostgresUniqueViolation.Is"/> must recognise a 23505 unique-constraint
/// violation surfaced through EF Core, optionally narrowed to a named index, and must not mistake an
/// unrelated <see cref="DbUpdateException"/> for one. Mirrors
/// HR.Modules.Notifications.Tests/PostgresUniqueViolationTests.cs for the module-local copy of the
/// same helper used by TaskCreator's idempotency-key save path.
/// </summary>
public class PostgresUniqueViolationTests
{
    private const string TaskIdempotencyIndex = "ix_task_items_company_id_idempotency_key";

    // Mimics Npgsql.PostgresException by type name + SqlState/ConstraintName members (reflection path).
    private sealed class PostgresException(string message, string sqlState, string? constraintName = null)
        : Exception(message)
    {
        public string SqlState { get; } = sqlState;
        public string? ConstraintName { get; } = constraintName;
    }

    private static DbUpdateException Wrap(Exception inner) => new("update failed", inner);

    [Fact]
    public void Recognises_duplicate_key_message()
    {
        var ex = Wrap(new Exception(
            $"23505: duplicate key value violates unique constraint \"{TaskIdempotencyIndex}\""));

        Assert.True(PostgresUniqueViolation.Is(ex));
        Assert.True(PostgresUniqueViolation.Is(ex, TaskIdempotencyIndex));
    }

    [Fact]
    public void Recognises_via_sqlstate_property()
    {
        var ex = Wrap(new PostgresException("boom", "23505", TaskIdempotencyIndex));

        Assert.True(PostgresUniqueViolation.Is(ex));
        Assert.True(PostgresUniqueViolation.Is(ex, TaskIdempotencyIndex));
    }

    [Fact]
    public void Constraint_narrowing_rejects_a_different_index()
    {
        var ex = Wrap(new Exception(
            "duplicate key value violates unique constraint \"IX_some_other_thing\""));

        Assert.True(PostgresUniqueViolation.Is(ex));
        Assert.False(PostgresUniqueViolation.Is(ex, TaskIdempotencyIndex));
    }

    [Fact]
    public void Constraint_narrowing_rejects_a_different_index_via_ConstraintName_property()
    {
        var ex = Wrap(new PostgresException("boom", "23505", "some_other_constraint"));

        Assert.True(PostgresUniqueViolation.Is(ex));
        Assert.False(PostgresUniqueViolation.Is(ex, TaskIdempotencyIndex));
    }

    [Fact]
    public void Ignores_unrelated_db_update_exception()
    {
        var ex = Wrap(new Exception(
            "null value in column \"title\" violates not-null constraint"));

        Assert.False(PostgresUniqueViolation.Is(ex));
        Assert.False(PostgresUniqueViolation.Is(ex, TaskIdempotencyIndex));
    }

    [Fact]
    public void Ignores_exception_with_no_inner()
        => Assert.False(PostgresUniqueViolation.Is(new DbUpdateException("no inner")));

    [Fact]
    public void Non_unique_sqlstate_is_not_a_match()
    {
        var ex = Wrap(new PostgresException("deadlock", "40P01"));
        Assert.False(PostgresUniqueViolation.Is(ex));
    }
}
