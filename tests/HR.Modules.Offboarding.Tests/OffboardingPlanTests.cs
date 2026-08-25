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
}
