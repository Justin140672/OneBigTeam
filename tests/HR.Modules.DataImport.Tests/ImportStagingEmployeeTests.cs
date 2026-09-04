using HR.Modules.DataImport.Domain;

namespace HR.Modules.DataImport.Tests;

/// <summary>
/// OBT-REM-08: unit coverage for the durable per-row confirmation-progress domain methods on
/// ImportStagingEmployee. A row is only IsFullyConfirmed once MarkFullyConfirmed has been called;
/// none of the individual Mark* steps imply completion of any other step.
/// </summary>
public class ImportStagingEmployeeTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    private static ImportStagingEmployee CreateRow() =>
        ImportStagingEmployee.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), rowNumber: 2,
            employeeNumber: "EMP-0001", workEmail: "alice@example.com", managerReference: null,
            departmentId: Guid.NewGuid(), locationId: Guid.NewGuid(),
            employmentTypeId: Guid.NewGuid(), positionProfileId: Guid.NewGuid(),
            rawData: "{}", isValid: true, now: FixedNow);

    [Fact]
    public void Create_Leaves_All_Progress_Fields_Null_And_Not_Fully_Confirmed()
    {
        var row = CreateRow();

        Assert.Null(row.CreatedEmployeeId);
        Assert.Null(row.EmployeeCreatedAt);
        Assert.Null(row.EmployeeCreatedEventPublishedAt);
        Assert.Null(row.EmployeeImportedEventPublishedAt);
        Assert.Null(row.OpeningLeaveBalanceProcessedAt);
        Assert.Null(row.ManagerAssignmentProcessedAt);
        Assert.Null(row.FullyConfirmedAt);
        Assert.False(row.IsFullyConfirmed);
    }

    [Fact]
    public void MarkEmployeeCreated_Sets_CreatedEmployeeId_And_EmployeeCreatedAt_Only()
    {
        var row = CreateRow();
        var employeeId = Guid.NewGuid();

        row.MarkEmployeeCreated(employeeId, FixedNow);

        Assert.Equal(employeeId, row.CreatedEmployeeId);
        Assert.Equal(FixedNow, row.EmployeeCreatedAt);
        Assert.Null(row.EmployeeCreatedEventPublishedAt);
        Assert.Null(row.EmployeeImportedEventPublishedAt);
        Assert.Null(row.OpeningLeaveBalanceProcessedAt);
        Assert.Null(row.ManagerAssignmentProcessedAt);
        Assert.False(row.IsFullyConfirmed);
    }

    [Fact]
    public void MarkEmployeeCreatedEventPublished_Sets_Only_That_Field()
    {
        var row = CreateRow();

        row.MarkEmployeeCreatedEventPublished(FixedNow);

        Assert.Equal(FixedNow, row.EmployeeCreatedEventPublishedAt);
        Assert.Null(row.EmployeeImportedEventPublishedAt);
        Assert.False(row.IsFullyConfirmed);
    }

    [Fact]
    public void MarkEmployeeImportedEventPublished_Sets_Only_That_Field()
    {
        var row = CreateRow();

        row.MarkEmployeeImportedEventPublished(FixedNow);

        Assert.Equal(FixedNow, row.EmployeeImportedEventPublishedAt);
        Assert.Null(row.EmployeeCreatedEventPublishedAt);
        Assert.False(row.IsFullyConfirmed);
    }

    [Fact]
    public void MarkOpeningLeaveBalanceProcessed_Sets_Only_That_Field()
    {
        var row = CreateRow();

        row.MarkOpeningLeaveBalanceProcessed(FixedNow);

        Assert.Equal(FixedNow, row.OpeningLeaveBalanceProcessedAt);
        Assert.Null(row.ManagerAssignmentProcessedAt);
        Assert.False(row.IsFullyConfirmed);
    }

    [Fact]
    public void MarkManagerAssignmentProcessed_Sets_Only_That_Field()
    {
        var row = CreateRow();

        row.MarkManagerAssignmentProcessed(FixedNow);

        Assert.Equal(FixedNow, row.ManagerAssignmentProcessedAt);
        Assert.Null(row.OpeningLeaveBalanceProcessedAt);
        Assert.False(row.IsFullyConfirmed);
    }

    [Fact]
    public void IsFullyConfirmed_Is_False_Until_Every_Step_Including_MarkFullyConfirmed_Has_Run()
    {
        var row = CreateRow();
        var employeeId = Guid.NewGuid();

        row.MarkEmployeeCreated(employeeId, FixedNow);
        Assert.False(row.IsFullyConfirmed);

        row.MarkEmployeeCreatedEventPublished(FixedNow);
        Assert.False(row.IsFullyConfirmed);

        row.MarkEmployeeImportedEventPublished(FixedNow);
        Assert.False(row.IsFullyConfirmed);

        row.MarkOpeningLeaveBalanceProcessed(FixedNow);
        Assert.False(row.IsFullyConfirmed);

        row.MarkManagerAssignmentProcessed(FixedNow);
        // Every individual step has now run, but the row is only fully confirmed once
        // MarkFullyConfirmed itself is explicitly called - it is not inferred from the other steps.
        Assert.False(row.IsFullyConfirmed);

        row.MarkFullyConfirmed(FixedNow);
        Assert.True(row.IsFullyConfirmed);
        Assert.Equal(FixedNow, row.FullyConfirmedAt);
    }

    [Fact]
    public void MarkFullyConfirmed_Can_Be_Called_Even_When_Some_Steps_Are_Still_Null()
    {
        // The domain method itself does not enforce the invariant (ConfirmImportSessionHandler
        // does, by only calling it once every mandatory field is non-null) - this documents that
        // the guard lives at the handler/orchestration layer, not in the entity.
        var row = CreateRow();

        row.MarkFullyConfirmed(FixedNow);

        Assert.True(row.IsFullyConfirmed);
        Assert.Null(row.CreatedEmployeeId);
    }
}
