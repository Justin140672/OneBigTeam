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
}
