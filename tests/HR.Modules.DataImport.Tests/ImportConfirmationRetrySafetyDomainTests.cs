using HR.Modules.DataImport.Domain;

namespace HR.Modules.DataImport.Tests;

/// <summary>
/// OBT-REM-06: domain-level guarantees for the retry/concurrency-safe confirm flow —
/// <see cref="ImportSession.ClaimForConfirmation"/> transitions + version bump, and the durable
/// per-row <see cref="ImportStagingEmployee.MarkConfirmed"/> marker that lets a retry skip a row.
/// </summary>
public class ImportConfirmationRetrySafetyDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 11, 53, 53, TimeSpan.Zero);

    private static ImportSession NewSession() => ImportSession.Create(
        Guid.NewGuid(), Guid.NewGuid(), "Employee", "e.csv", 3, Guid.NewGuid(),
        "k", "text/csv", Now);

    [Fact]
    public void New_session_starts_at_version_one()
        => Assert.Equal(1, NewSession().Version);

    [Fact]
    public void ClaimForConfirmation_moves_to_Processing_and_bumps_version()
    {
        var s = NewSession();
        s.Start(Now);
        s.Validate(successfulRows: 3, failedRows: 0, Now);
        var versionBefore = s.Version;

        s.ClaimForConfirmation(Now.AddMinutes(1));

        Assert.Equal(ImportStatus.Processing, s.Status);
        Assert.Equal(versionBefore + 1, s.Version);
        Assert.Null(s.CompletedAt);
        Assert.Equal(Now.AddMinutes(1), s.UpdatedAt);
    }

    [Fact]
    public void ClaimForConfirmation_preserves_original_StartedAt()
    {
        var s = NewSession();
        s.Start(Now);
        var originalStart = s.StartedAt;

        s.ClaimForConfirmation(Now.AddHours(2));

        Assert.Equal(originalStart, s.StartedAt);
    }

    [Fact]
    public void ClaimForConfirmation_sets_StartedAt_when_not_yet_started()
    {
        var s = NewSession();

        s.ClaimForConfirmation(Now.AddMinutes(5));

        Assert.Equal(Now.AddMinutes(5), s.StartedAt);
    }

    [Fact]
    public void Claim_then_Confirm_bumps_version_twice()
    {
        var s = NewSession();
        s.Start(Now);
        s.Validate(3, 0, Now);
        var v0 = s.Version;

        s.ClaimForConfirmation(Now);
        s.Confirm(createdCount: 3, failedCount: 0, Now);

        Assert.Equal(v0 + 2, s.Version);
    }

    [Fact]
    public void MarkConfirmed_records_created_employee_and_timestamp()
    {
        var row = ImportStagingEmployee.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, "EMP-1", "a@x.com", null,
            null, null, null, null, "{}", isValid: true, Now);

        Assert.Null(row.CreatedEmployeeId);
        Assert.Null(row.ConfirmedAt);

        var employeeId = Guid.NewGuid();
        row.MarkConfirmed(employeeId, Now.AddMinutes(1));

        Assert.Equal(employeeId, row.CreatedEmployeeId);
        Assert.Equal(Now.AddMinutes(1), row.ConfirmedAt);
    }

    [Fact]
    public void MarkConfirmed_is_the_signal_a_retry_uses_to_skip_a_row()
    {
        // A row that already produced an employee must report a non-null CreatedEmployeeId so the
        // handler's "only rows with CreatedEmployeeId == null" filter excludes it on retry.
        var row = ImportStagingEmployee.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5, null, null, null,
            null, null, null, null, "{}", isValid: true, Now);
        row.MarkConfirmed(Guid.NewGuid(), Now);

        Assert.NotNull(row.CreatedEmployeeId);
    }
}
