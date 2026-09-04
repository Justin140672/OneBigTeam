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
    public void ClaimForConfirmation_refreshes_StartedAt_on_every_claim()
    {
        // OBT-REM-08: StartedAt must be refreshed on every claim (not just the first), so an
        // actively running confirmation is judged for staleness from when THIS attempt started,
        // not from a much earlier Validate/first-claim timestamp.
        var s = NewSession();
        s.Start(Now);

        s.ClaimForConfirmation(Now.AddHours(2));

        Assert.Equal(Now.AddHours(2), s.StartedAt);
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
    public void MarkEmployeeCreated_records_created_employee_and_timestamp()
    {
        var row = ImportStagingEmployee.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, "EMP-1", "a@x.com", null,
            null, null, null, null, "{}", isValid: true, Now);

        Assert.Null(row.CreatedEmployeeId);
        Assert.Null(row.FullyConfirmedAt);
        Assert.False(row.IsFullyConfirmed);

        var employeeId = Guid.NewGuid();
        row.MarkEmployeeCreated(employeeId, Now.AddMinutes(1));

        Assert.Equal(employeeId, row.CreatedEmployeeId);
        Assert.Equal(Now.AddMinutes(1), row.EmployeeCreatedAt);
        // Creating the employee alone does not yet make the row fully confirmed — downstream
        // steps (events, leave balance, manager assignment) still need to complete.
        Assert.False(row.IsFullyConfirmed);
    }

    [Fact]
    public void MarkEmployeeCreated_is_the_signal_a_retry_uses_to_skip_re_creating_the_employee()
    {
        // A row that already produced an employee must report a non-null CreatedEmployeeId so the
        // handler's resume path reads back the snapshot instead of creating a second employee.
        var row = ImportStagingEmployee.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5, null, null, null,
            null, null, null, null, "{}", isValid: true, Now);
        row.MarkEmployeeCreated(Guid.NewGuid(), Now);

        Assert.NotNull(row.CreatedEmployeeId);
    }

    [Fact]
    public void IsFullyConfirmed_requires_every_mandatory_step()
    {
        var row = ImportStagingEmployee.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 7, null, null, null,
            null, null, null, null, "{}", isValid: true, Now);

        row.MarkEmployeeCreated(Guid.NewGuid(), Now);
        row.MarkEmployeeCreatedEventPublished(Now);
        row.MarkEmployeeImportedEventPublished(Now);
        row.MarkOpeningLeaveBalanceProcessed(Now);
        Assert.False(row.IsFullyConfirmed);

        row.MarkManagerAssignmentProcessed(Now);
        Assert.False(row.IsFullyConfirmed);

        row.MarkFullyConfirmed(Now.AddMinutes(1));

        Assert.True(row.IsFullyConfirmed);
        Assert.Equal(Now.AddMinutes(1), row.FullyConfirmedAt);
    }
}
