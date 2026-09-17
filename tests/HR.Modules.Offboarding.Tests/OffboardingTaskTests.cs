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
    public void CreateWaived_Produces_A_Waived_Task_With_Null_CompletedAt_And_Given_Description()
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
        // SPEC-OFF-01: CreateWaived now routes through Waive() rather than the legacy Skip() path,
        // so system-auto-resolved tasks get Status=Waived, not Status=Skipped.
        Assert.Equal(OffboardingTaskStatus.Waived, task.Status);
        // Waive() does not set CompletedAt (see Waive_Sets_Status_SkipReason_Actor_And_SkippedAt below).
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
        Assert.Equal(OffboardingTaskStatus.Waived, task.Status);
        Assert.Equal(description, task.SkipReason);
        Assert.Equal(Guid.Empty, task.SkippedByUserId); // OffboardingSystemActor.Id
        Assert.Equal(FixedNow, task.SkippedAt);
    }

    // ---- SPEC-OFF-01: Waive ----

    [Fact]
    public void Waive_Sets_Status_SkipReason_Actor_And_SkippedAt()
    {
        var task = OffboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Return laptop", null,
            OffboardingTaskAssignTo.Employee, null, FixedNow);
        var later = FixedNow.AddDays(1);
        var actorUserId = Guid.NewGuid();

        task.Waive(later, "Not required for this departure.", actorUserId);

        Assert.Equal(OffboardingTaskStatus.Waived, task.Status);
        Assert.Equal("Not required for this departure.", task.SkipReason);
        Assert.Equal(actorUserId, task.SkippedByUserId);
        Assert.Equal(later, task.SkippedAt);
        Assert.Null(task.CompletedAt);
        Assert.Equal(later, task.UpdatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Waive_Throws_ArgumentException_When_Reason_Is_Null_Empty_Or_Whitespace(string? reason)
    {
        var task = OffboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Return laptop", null,
            OffboardingTaskAssignTo.Employee, null, FixedNow);

        var ex = Assert.Throws<ArgumentException>(() => task.Waive(FixedNow.AddDays(1), reason!, Guid.NewGuid()));
        Assert.Equal("reason", ex.ParamName);

        Assert.Equal(OffboardingTaskStatus.Pending, task.Status);
        Assert.Null(task.SkipReason);
        Assert.Null(task.SkippedByUserId);
        Assert.Null(task.SkippedAt);
        Assert.Equal(FixedNow, task.UpdatedAt);
    }

    [Theory]
    [InlineData((int)OffboardingTaskStatus.Completed)]
    [InlineData((int)OffboardingTaskStatus.Waived)]
    [InlineData((int)OffboardingTaskStatus.Cancelled)]
    public void Waive_Throws_InvalidOperationException_When_Already_Terminal(int terminalStatusValue)
    {
        var terminalStatus = (OffboardingTaskStatus)terminalStatusValue;
        var task = OffboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Return laptop", null,
            OffboardingTaskAssignTo.Employee, null, FixedNow);
        MoveToStatus(task, terminalStatus, FixedNow.AddDays(1));

        var ex = Assert.Throws<InvalidOperationException>(
            () => task.Waive(FixedNow.AddDays(2), "Some reason.", Guid.NewGuid()));

        Assert.Equal($"Cannot waive an offboarding task with status '{terminalStatus}'.", ex.Message);
    }

    // Pending/InProgress are the only non-terminal statuses a task can be waived from — this pins
    // the negated branch of the guard (i.e. it must NOT throw) for both of them, rather than only
    // exercising the "not yet started" (Pending) branch implicitly covered by the other Waive tests.
    [Fact]
    public void Waive_Succeeds_From_Pending_Status()
    {
        var task = OffboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Return laptop", null,
            OffboardingTaskAssignTo.Employee, null, FixedNow);

        var exception = Record.Exception(() => task.Waive(FixedNow.AddDays(1), "Not required.", Guid.NewGuid()));

        Assert.Null(exception);
        Assert.Equal(OffboardingTaskStatus.Waived, task.Status);
    }

    // ---- SPEC-OFF-01: CancelBecauseLeavingProcessCancelled ----

    [Fact]
    public void CancelBecauseLeavingProcessCancelled_Sets_Status_Cancelled_With_Fixed_Reason_And_Actor()
    {
        var task = OffboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Return laptop", null,
            OffboardingTaskAssignTo.Employee, null, FixedNow);
        var later = FixedNow.AddDays(1);
        var actorUserId = Guid.NewGuid();

        task.CancelBecauseLeavingProcessCancelled(later, actorUserId);

        Assert.Equal(OffboardingTaskStatus.Cancelled, task.Status);
        Assert.Equal("Leaving process cancelled.", task.SkipReason);
        Assert.Equal(actorUserId, task.SkippedByUserId);
        Assert.Equal(later, task.SkippedAt);
        Assert.Equal(later, task.UpdatedAt);
    }

    [Theory]
    [InlineData((int)OffboardingTaskStatus.Completed)]
    [InlineData((int)OffboardingTaskStatus.Waived)]
    [InlineData((int)OffboardingTaskStatus.Cancelled)]
    public void CancelBecauseLeavingProcessCancelled_Is_NoOp_When_Already_Terminal(int terminalStatusValue)
    {
        var terminalStatus = (OffboardingTaskStatus)terminalStatusValue;
        var task = OffboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Return laptop", null,
            OffboardingTaskAssignTo.Employee, null, FixedNow);
        var terminalAt = FixedNow.AddDays(1);
        MoveToStatus(task, terminalStatus, terminalAt);
        var originalSkipReason = task.SkipReason;
        var originalSkippedByUserId = task.SkippedByUserId;

        var exception = Record.Exception(
            () => task.CancelBecauseLeavingProcessCancelled(FixedNow.AddDays(5), Guid.NewGuid()));

        Assert.Null(exception);
        Assert.Equal(terminalStatus, task.Status);
        Assert.Equal(originalSkipReason, task.SkipReason);
        Assert.Equal(originalSkippedByUserId, task.SkippedByUserId);
        // No-op must not bump UpdatedAt past when the task actually reached its terminal state.
        Assert.Equal(terminalAt, task.UpdatedAt);
    }

    private static void MoveToStatus(OffboardingTask task, OffboardingTaskStatus status, DateTimeOffset at)
    {
        switch (status)
        {
            case OffboardingTaskStatus.Completed:
                task.Complete(at);
                break;
            case OffboardingTaskStatus.Waived:
                task.Waive(at, "Not required.", Guid.NewGuid());
                break;
            case OffboardingTaskStatus.Cancelled:
                task.CancelBecauseLeavingProcessCancelled(at, Guid.NewGuid());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }
    }
}
