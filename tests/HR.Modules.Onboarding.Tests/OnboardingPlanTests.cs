using HR.Modules.Onboarding.Domain;

namespace HR.Modules.Onboarding.Tests;

public class OnboardingPlanTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_Sets_Initial_State_To_NotStarted()
    {
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var startDate = new DateOnly(2026, 7, 1);

        var plan = OnboardingPlan.Create(id, companyId, employeeId, startDate, "Welcome aboard.", FixedNow);

        Assert.Equal(id, plan.Id);
        Assert.Equal(companyId, plan.CompanyId);
        Assert.Equal(employeeId, plan.EmployeeId);
        Assert.Equal(startDate, plan.StartDate);
        Assert.Equal(OnboardingStatus.NotStarted, plan.Status);
        Assert.Equal("Welcome aboard.", plan.Notes);
        Assert.Equal(FixedNow, plan.CreatedAt);
        Assert.Equal(FixedNow, plan.UpdatedAt);
    }

    [Fact]
    public void Create_Allows_Null_Notes()
    {
        var plan = OnboardingPlan.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), null, FixedNow);

        Assert.Null(plan.Notes);
    }

    [Fact]
    public void Start_Transitions_Status_To_InProgress_And_Updates_Timestamp()
    {
        var plan = OnboardingPlan.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), null, FixedNow);
        var later = FixedNow.AddDays(1);

        plan.Start(later);

        Assert.Equal(OnboardingStatus.InProgress, plan.Status);
        Assert.Equal(later, plan.UpdatedAt);
    }

    [Fact]
    public void Complete_Transitions_Status_To_Completed_And_Updates_Timestamp()
    {
        var plan = OnboardingPlan.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), null, FixedNow);
        plan.Start(FixedNow);
        var later = FixedNow.AddDays(30);

        plan.Complete(later);

        Assert.Equal(OnboardingStatus.Completed, plan.Status);
        Assert.Equal(later, plan.UpdatedAt);
    }

    [Fact]
    public void Cancel_Transitions_Status_To_Cancelled_And_Updates_Notes_And_Timestamp()
    {
        var plan = OnboardingPlan.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), "Initial notes.", FixedNow);
        var later = FixedNow.AddDays(2);

        plan.Cancel("Employee withdrew offer.", later);

        Assert.Equal(OnboardingStatus.Cancelled, plan.Status);
        Assert.Equal("Employee withdrew offer.", plan.Notes);
        Assert.Equal(later, plan.UpdatedAt);
    }

    [Fact]
    public void Cancel_Allows_Null_Notes()
    {
        var plan = OnboardingPlan.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), "Initial notes.", FixedNow);

        plan.Cancel(null, FixedNow.AddDays(1));

        Assert.Equal(OnboardingStatus.Cancelled, plan.Status);
        Assert.Null(plan.Notes);
    }
}
