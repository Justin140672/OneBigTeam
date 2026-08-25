using HR.Modules.Offboarding.Domain;

namespace HR.Modules.Offboarding.Tests;

public class OffboardingTaskTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_Sets_Initial_State_To_Pending()
    {
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var dueDate = new DateOnly(2026, 7, 1);

        var task = OffboardingTask.Create(
            id, companyId, planId, "Return laptop", "Return company laptop before last day.",
            OffboardingTaskAssignTo.Employee, dueDate, FixedNow);

        Assert.Equal(id, task.Id);
        Assert.Equal(companyId, task.CompanyId);
        Assert.Equal(planId, task.OffboardingPlanId);
        Assert.Equal("Return laptop", task.Title);
        Assert.Equal("Return company laptop before last day.", task.Description);
        Assert.Equal(OffboardingTaskAssignTo.Employee, task.AssignTo);
        Assert.Equal(dueDate, task.DueDate);
        Assert.Equal(OffboardingTaskStatus.Pending, task.Status);
        Assert.Null(task.CompletedAt);
        Assert.Equal(FixedNow, task.CreatedAt);
        Assert.Equal(FixedNow, task.UpdatedAt);
    }

    // OFF-03
    [Fact]
    public void Create_Defaults_AssignedEmployeeId_To_Null_When_Not_Supplied()
    {
        var task = OffboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Return laptop", null,
            OffboardingTaskAssignTo.Employee, null, FixedNow);

        Assert.Null(task.AssignedEmployeeId);
    }

    // OFF-03
    [Fact]
    public void Create_Sets_AssignedEmployeeId_When_Supplied()
    {
        var assignedEmployeeId = Guid.NewGuid();

        var task = OffboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Return laptop", null,
            OffboardingTaskAssignTo.Employee, null, FixedNow, assignedEmployeeId);

        Assert.Equal(assignedEmployeeId, task.AssignedEmployeeId);
    }

    // OFF-03
    [Fact]
    public void MarkTaskItemCreated_Sets_TaskItemCreatedAt_And_UpdatedAt()
    {
        var task = OffboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Return laptop", null,
            OffboardingTaskAssignTo.Employee, null, FixedNow);
        var later = FixedNow.AddDays(1);

        task.MarkTaskItemCreated(later);

        Assert.Equal(later, task.TaskItemCreatedAt);
        Assert.Equal(later, task.UpdatedAt);
    }

    [Fact]
    public void Create_Allows_Null_Description_And_Null_DueDate()
    {
        var task = OffboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Conduct exit interview", null,
            OffboardingTaskAssignTo.Manager, null, FixedNow);

        Assert.Null(task.Description);
        Assert.Null(task.DueDate);
    }

    [Fact]
    public void Complete_Sets_CompletedAt_Status_And_UpdatedAt()
    {
        var task = OffboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Return laptop", null,
            OffboardingTaskAssignTo.Employee, null, FixedNow);
        var later = FixedNow.AddDays(1);

        task.Complete(later);

        Assert.Equal(OffboardingTaskStatus.Completed, task.Status);
        Assert.Equal(later, task.CompletedAt);
        Assert.Equal(later, task.UpdatedAt);
    }

    [Fact]
    public void Skip_Sets_Status_And_UpdatedAt_But_Leaves_CompletedAt_Null()
    {
        var task = OffboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Return laptop", null,
            OffboardingTaskAssignTo.Employee, null, FixedNow);
        var later = FixedNow.AddDays(1);

        task.Skip(later, "No longer applicable.", Guid.NewGuid());

        Assert.Equal(OffboardingTaskStatus.Skipped, task.Status);
        Assert.Null(task.CompletedAt);
        Assert.Equal(later, task.UpdatedAt);
    }

    // OFF-02
    [Fact]
    public void Reschedule_Updates_DueDate_And_UpdatedAt_When_Date_Changes()
    {
        var task = OffboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Return laptop", null,
            OffboardingTaskAssignTo.Employee, new DateOnly(2026, 7, 1), FixedNow);
        var later = FixedNow.AddDays(1);
        var newDueDate = new DateOnly(2026, 7, 15);

        var changed = task.Reschedule(newDueDate, later);

        Assert.True(changed);
        Assert.Equal(newDueDate, task.DueDate);
        Assert.Equal(later, task.UpdatedAt);
    }

    [Fact]
    public void Reschedule_Is_NoOp_When_New_Date_Equals_Current_DueDate()
    {
        var dueDate = new DateOnly(2026, 7, 1);
        var task = OffboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Return laptop", null,
            OffboardingTaskAssignTo.Employee, dueDate, FixedNow);
        var later = FixedNow.AddDays(1);

        var changed = task.Reschedule(dueDate, later);

        Assert.False(changed);
        Assert.Equal(dueDate, task.DueDate);
        Assert.Equal(FixedNow, task.UpdatedAt);
    }

    [Fact]
    public void Reschedule_Works_When_Current_DueDate_Is_Null()
    {
        var task = OffboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Conduct exit interview", null,
            OffboardingTaskAssignTo.Manager, null, FixedNow);
        var later = FixedNow.AddDays(1);
        var newDueDate = new DateOnly(2026, 7, 15);

        var changed = task.Reschedule(newDueDate, later);

        Assert.True(changed);
        Assert.Equal(newDueDate, task.DueDate);
        Assert.Equal(later, task.UpdatedAt);
    }

    // ---- OFF-05 ----

    [Fact]
    public void Create_Defaults_RequiresHrConfirmation_False_When_Not_Supplied()
    {
        var task = OffboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Return laptop", null,
            OffboardingTaskAssignTo.Employee, null, FixedNow);

        Assert.False(task.RequiresHrConfirmation);
    }

    [Fact]
    public void Create_Sets_RequiresHrConfirmation_When_Supplied()
    {
        var task = OffboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Return laptop", null,
            OffboardingTaskAssignTo.HR, null, FixedNow, requiresHrConfirmation: true);

        Assert.True(task.RequiresHrConfirmation);
    }

    [Fact]
    public void CreateWaived_Produces_A_Skipped_Task_With_Null_CompletedAt_And_Given_Description()
    {
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var dueDate = new DateOnly(2026, 7, 1);
        const string description = "Waived automatically — access already disabled.";

        var task = OffboardingTask.CreateWaived(
            id, companyId, planId, "Revoke system access and accounts — Jamie Smith", description,
            OffboardingTaskAssignTo.Manager, dueDate, FixedNow);

        Assert.Equal(id, task.Id);
        Assert.Equal(companyId, task.CompanyId);
        Assert.Equal(planId, task.OffboardingPlanId);
        Assert.Equal(OffboardingTaskStatus.Skipped, task.Status);
        // Skip() does not set CompletedAt (see Skip_Sets_Status_And_UpdatedAt_But_Leaves_CompletedAt_Null above).
        Assert.Null(task.CompletedAt);
        Assert.Equal(description, task.Description);
        Assert.Equal(FixedNow, task.UpdatedAt);
    }

    // ---- OFF-07 ----

    [Fact]
    public void Create_Defaults_IsMandatory_True_When_Not_Supplied()
    {
        var task = OffboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Return laptop", null,
            OffboardingTaskAssignTo.Employee, null, FixedNow);

        Assert.True(task.IsMandatory);
    }

    [Fact]
    public void Create_Sets_IsMandatory_False_When_Explicitly_Supplied()
    {
        var task = OffboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Optional handover note", null,
            OffboardingTaskAssignTo.Manager, null, FixedNow, isMandatory: false);

        Assert.False(task.IsMandatory);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Skip_Throws_ArgumentException_When_Reason_Is_Null_Empty_Or_Whitespace(string? reason)
    {
        var task = OffboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Return laptop", null,
            OffboardingTaskAssignTo.Employee, null, FixedNow);

        var ex = Assert.Throws<ArgumentException>(() => task.Skip(FixedNow.AddDays(1), reason!, Guid.NewGuid()));
        Assert.Equal("reason", ex.ParamName);

        // The task must be left completely untouched — the exception is thrown before any state change.
        Assert.Equal(OffboardingTaskStatus.Pending, task.Status);
        Assert.Null(task.SkipReason);
        Assert.Null(task.SkippedByUserId);
        Assert.Null(task.SkippedAt);
        Assert.Equal(FixedNow, task.UpdatedAt);
    }

    [Fact]
    public void Skip_Populates_SkipReason_SkippedByUserId_And_SkippedAt()
    {
        var task = OffboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Return laptop", null,
            OffboardingTaskAssignTo.Employee, null, FixedNow);
        var later = FixedNow.AddDays(1);
        var actorUserId = Guid.NewGuid();

        task.Skip(later, "Employee already handed it in.", actorUserId);

        Assert.Equal(OffboardingTaskStatus.Skipped, task.Status);
        Assert.Equal("Employee already handed it in.", task.SkipReason);
        Assert.Equal(actorUserId, task.SkippedByUserId);
        Assert.Equal(later, task.SkippedAt);
        Assert.Equal(later, task.UpdatedAt);
    }

    [Fact]
    public void CreateWaived_Produces_IsMandatory_False_With_SkipReason_And_SystemActor()
    {
        const string description = "Waived automatically — access already disabled.";

        var task = OffboardingTask.CreateWaived(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Revoke system access and accounts — Jamie Smith",
            description, OffboardingTaskAssignTo.Manager, new DateOnly(2026, 7, 1), FixedNow);

        Assert.False(task.IsMandatory);
        Assert.Equal(OffboardingTaskStatus.Skipped, task.Status);
        Assert.Equal(description, task.SkipReason);
        Assert.Equal(Guid.Empty, task.SkippedByUserId); // OffboardingSystemActor.Id
        Assert.Equal(FixedNow, task.SkippedAt);
    }
}
