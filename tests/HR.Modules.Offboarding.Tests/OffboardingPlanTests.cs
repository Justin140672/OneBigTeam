using HR.Modules.Offboarding.Domain;

namespace HR.Modules.Offboarding.Tests;

public class OffboardingPlanTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_Sets_Initial_State_To_NotStarted()
    {
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var lastWorkingDay = new DateOnly(2026, 7, 1);

        var plan = OffboardingPlan.Create(id, companyId, employeeId, lastWorkingDay, "Notice given.", FixedNow);

        Assert.Equal(id, plan.Id);
        Assert.Equal(companyId, plan.CompanyId);
        Assert.Equal(employeeId, plan.EmployeeId);
        Assert.Equal(lastWorkingDay, plan.LastWorkingDay);
        Assert.Equal(OffboardingStatus.NotStarted, plan.Status);
        Assert.Equal("Notice given.", plan.Notes);
        Assert.Equal(FixedNow, plan.CreatedAt);
        Assert.Equal(FixedNow, plan.UpdatedAt);
    }

    [Fact]
    public void Create_Allows_Null_Notes()
    {
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), null, FixedNow);

        Assert.Null(plan.Notes);
    }

    [Fact]
    public void Start_Transitions_Status_To_InProgress_And_Updates_Timestamp()
    {
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), null, FixedNow);
        var later = FixedNow.AddDays(1);

        plan.Start(later);

        Assert.Equal(OffboardingStatus.InProgress, plan.Status);
        Assert.Equal(later, plan.UpdatedAt);
    }

    [Fact]
    public void Complete_Transitions_Status_To_Completed_And_Updates_Timestamp()
    {
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), null, FixedNow);
        plan.Start(FixedNow);
        var later = FixedNow.AddDays(30);

        plan.Complete(later);

        Assert.Equal(OffboardingStatus.Completed, plan.Status);
        Assert.Equal(later, plan.UpdatedAt);
    }

    [Fact]
    public void Cancel_Transitions_Status_To_Cancelled_And_Updates_Notes_And_Timestamp()
    {
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), "Initial notes.", FixedNow);
        var later = FixedNow.AddDays(2);

        plan.Cancel("Employee retracted resignation.", later);

        Assert.Equal(OffboardingStatus.Cancelled, plan.Status);
        Assert.Equal("Employee retracted resignation.", plan.Notes);
        Assert.Equal(later, plan.UpdatedAt);
    }

    [Fact]
    public void Cancel_Allows_Null_Notes()
    {
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), "Initial notes.", FixedNow);

        plan.Cancel(null, FixedNow.AddDays(1));

        Assert.Equal(OffboardingStatus.Cancelled, plan.Status);
        Assert.Null(plan.Notes);
    }

    // OFF-02
    [Fact]
    public void Reschedule_Updates_LastWorkingDay_And_UpdatedAt_When_Date_Changes()
    {
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), null, FixedNow);
        var later = FixedNow.AddDays(1);
        var newLastWorkingDay = new DateOnly(2026, 7, 15);

        var changed = plan.Reschedule(newLastWorkingDay, later);

        Assert.True(changed);
        Assert.Equal(newLastWorkingDay, plan.LastWorkingDay);
        Assert.Equal(later, plan.UpdatedAt);
    }

    [Fact]
    public void Reschedule_Is_NoOp_When_New_Date_Equals_Current_LastWorkingDay()
    {
        var lastWorkingDay = new DateOnly(2026, 7, 1);
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), lastWorkingDay, null, FixedNow);
        var later = FixedNow.AddDays(1);

        var changed = plan.Reschedule(lastWorkingDay, later);

        Assert.False(changed);
        Assert.Equal(lastWorkingDay, plan.LastWorkingDay);
        Assert.Equal(FixedNow, plan.UpdatedAt);
    }

    // ---- OFF-05 ----

    [Fact]
    public void Create_Defaults_IsBackdated_False_And_RequiresHrReconciliation_False_When_Not_Specified()
    {
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), null, FixedNow);

        Assert.False(plan.IsBackdated);
        Assert.False(plan.RequiresHrReconciliation);
    }

    [Fact]
    public void Create_Sets_IsBackdated_True_When_Specified()
    {
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), null, FixedNow,
            isBackdated: true);

        Assert.True(plan.IsBackdated);
    }

    [Fact]
    public void MarkHrReconciliationRequired_Sets_Flag_And_Bumps_UpdatedAt()
    {
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), null, FixedNow,
            isBackdated: true);
        var later = FixedNow.AddMinutes(5);

        plan.MarkHrReconciliationRequired(later);

        Assert.True(plan.RequiresHrReconciliation);
        Assert.Equal(later, plan.UpdatedAt);
    }

    [Fact]
    public void ResolveHrReconciliation_Clears_Flag_And_Bumps_UpdatedAt_When_Currently_True()
    {
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), null, FixedNow,
            isBackdated: true);
        plan.MarkHrReconciliationRequired(FixedNow.AddMinutes(5));
        var later = FixedNow.AddDays(1);

        plan.ResolveHrReconciliation(later);

        Assert.False(plan.RequiresHrReconciliation);
        Assert.Equal(later, plan.UpdatedAt);
    }

    [Fact]
    public void ResolveHrReconciliation_Is_NoOp_When_Flag_Already_False()
    {
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), null, FixedNow);
        var later = FixedNow.AddDays(1);

        plan.ResolveHrReconciliation(later);

        Assert.False(plan.RequiresHrReconciliation);
        // Guard returns early without touching UpdatedAt when the flag was already false.
        Assert.Equal(FixedNow, plan.UpdatedAt);
    }

    // ---- OFF-07: CanComplete ----

    [Fact]
    public void CanComplete_Returns_False_For_Empty_Task_List()
    {
        Assert.False(OffboardingPlan.CanComplete([]));
    }

    [Fact]
    public void CanComplete_Returns_True_When_All_Mandatory_Tasks_Completed()
    {
        var taskA = MandatoryTask();
        taskA.Complete(FixedNow);
        var taskB = MandatoryTask();
        taskB.Complete(FixedNow);

        Assert.True(OffboardingPlan.CanComplete([taskA, taskB]));
    }

    [Fact]
    public void CanComplete_Returns_False_When_A_Mandatory_Task_Is_Skipped_Not_Completed()
    {
        var completedMandatory = MandatoryTask();
        completedMandatory.Complete(FixedNow);
        var skippedMandatory = MandatoryTask();
        skippedMandatory.Skip(FixedNow, "Not applicable.", Guid.NewGuid());

        Assert.False(OffboardingPlan.CanComplete([completedMandatory, skippedMandatory]));
    }

    [Fact]
    public void CanComplete_Returns_True_When_Mandatory_Tasks_Completed_And_Optional_Tasks_Completed_Or_Skipped()
    {
        var mandatoryTask = MandatoryTask();
        mandatoryTask.Complete(FixedNow);
        var optionalCompleted = OptionalTask();
        optionalCompleted.Complete(FixedNow);
        var optionalSkipped = OptionalTask();
        optionalSkipped.Skip(FixedNow, "Not applicable.", Guid.NewGuid());

        Assert.True(OffboardingPlan.CanComplete([mandatoryTask, optionalCompleted, optionalSkipped]));
    }

    [Fact]
    public void CanComplete_Returns_False_When_An_Optional_Task_Is_Still_Pending()
    {
        var mandatoryTask = MandatoryTask();
        mandatoryTask.Complete(FixedNow);
        var optionalPending = OptionalTask();

        Assert.False(OffboardingPlan.CanComplete([mandatoryTask, optionalPending]));
    }

    private static OffboardingTask MandatoryTask() =>
        OffboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Mandatory task", null,
            OffboardingTaskAssignTo.Employee, null, FixedNow);

    private static OffboardingTask OptionalTask() =>
        OffboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Optional task", null,
            OffboardingTaskAssignTo.Employee, null, FixedNow, isMandatory: false);

    // ---- OFF-07: HasIncompleteOffboardingAtDeparture ----

    [Fact]
    public void MarkIncompleteOffboardingAtDeparture_Sets_Flag_And_Bumps_UpdatedAt()
    {
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), null, FixedNow);
        var later = FixedNow.AddDays(1);

        plan.MarkIncompleteOffboardingAtDeparture(later);

        Assert.True(plan.HasIncompleteOffboardingAtDeparture);
        Assert.Equal(later, plan.UpdatedAt);
    }

    [Fact]
    public void MarkIncompleteOffboardingAtDeparture_Is_Idempotent_When_Already_Flagged()
    {
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), null, FixedNow);
        var firstCallAt = FixedNow.AddDays(1);
        plan.MarkIncompleteOffboardingAtDeparture(firstCallAt);

        plan.MarkIncompleteOffboardingAtDeparture(FixedNow.AddDays(5));

        Assert.True(plan.HasIncompleteOffboardingAtDeparture);
        Assert.Equal(firstCallAt, plan.UpdatedAt); // No spurious UpdatedAt bump on the repeat call.
    }

    [Fact]
    public void ResolveIncompleteOffboardingAtDeparture_Clears_Flag_And_Bumps_UpdatedAt_When_Currently_True()
    {
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), null, FixedNow);
        plan.MarkIncompleteOffboardingAtDeparture(FixedNow.AddDays(1));
        var later = FixedNow.AddDays(2);

        plan.ResolveIncompleteOffboardingAtDeparture(later);

        Assert.False(plan.HasIncompleteOffboardingAtDeparture);
        Assert.Equal(later, plan.UpdatedAt);
    }

    [Fact]
    public void ResolveIncompleteOffboardingAtDeparture_Is_NoOp_When_Flag_Already_False()
    {
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), null, FixedNow);
        var later = FixedNow.AddDays(1);

        plan.ResolveIncompleteOffboardingAtDeparture(later);

        Assert.False(plan.HasIncompleteOffboardingAtDeparture);
        Assert.Equal(FixedNow, plan.UpdatedAt);
    }

    // ---- OFF-07: TryClaimFinalReviewTaskCreation ----

    [Fact]
    public void TryClaimFinalReviewTaskCreation_Returns_True_And_Sets_Timestamp_On_First_Call()
    {
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), null, FixedNow);
        var claimedAt = FixedNow.AddDays(1);

        var claimed = plan.TryClaimFinalReviewTaskCreation(claimedAt);

        Assert.True(claimed);
        Assert.Equal(claimedAt, plan.FinalReviewTaskCreatedAt);
        Assert.Equal(claimedAt, plan.UpdatedAt);
    }

    [Fact]
    public void TryClaimFinalReviewTaskCreation_Returns_False_And_Leaves_State_Untouched_On_Repeat_Calls()
    {
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), null, FixedNow);
        var firstClaimAt = FixedNow.AddDays(1);
        var firstClaim = plan.TryClaimFinalReviewTaskCreation(firstClaimAt);

        var secondClaim = plan.TryClaimFinalReviewTaskCreation(FixedNow.AddDays(5));
        var thirdClaim = plan.TryClaimFinalReviewTaskCreation(FixedNow.AddDays(10));

        Assert.True(firstClaim);
        Assert.False(secondClaim);
        Assert.False(thirdClaim);
        Assert.Equal(firstClaimAt, plan.FinalReviewTaskCreatedAt);
        Assert.Equal(firstClaimAt, plan.UpdatedAt);
    }
}
