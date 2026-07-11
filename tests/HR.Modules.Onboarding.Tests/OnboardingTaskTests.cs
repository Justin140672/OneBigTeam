using HR.Infrastructure.Abstractions;
using HR.Modules.Onboarding.Domain;

namespace HR.Modules.Onboarding.Tests;

public class OnboardingTaskTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_Sets_Initial_State_To_Pending()
    {
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var dueDate = new DateOnly(2026, 7, 1);

        var task = OnboardingTask.Create(
            id, companyId, planId, "Set up workstation", "Provision laptop.",
            OnboardingTemplateTaskAssignTo.Unassigned, dueDate, FixedNow);

        Assert.Equal(id, task.Id);
        Assert.Equal(companyId, task.CompanyId);
        Assert.Equal(planId, task.OnboardingPlanId);
        Assert.Equal("Set up workstation", task.Title);
        Assert.Equal("Provision laptop.", task.Description);
        Assert.Equal(dueDate, task.DueDate);
        Assert.Equal(OnboardingTaskStatus.Pending, task.Status);
        Assert.Null(task.CompletedAt);
        Assert.Equal(FixedNow, task.CreatedAt);
        Assert.Equal(FixedNow, task.UpdatedAt);
    }

    [Fact]
    public void Complete_Sets_CompletedAt_Status_And_UpdatedAt()
    {
        var task = OnboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Set up workstation", null,
            OnboardingTemplateTaskAssignTo.Unassigned, null, FixedNow);
        var later = FixedNow.AddDays(1);

        task.Complete(later);

        Assert.Equal(OnboardingTaskStatus.Completed, task.Status);
        Assert.Equal(later, task.CompletedAt);
        Assert.Equal(later, task.UpdatedAt);
    }

    [Fact]
    public void Skip_Sets_Status_And_UpdatedAt_But_Leaves_CompletedAt_Null()
    {
        var task = OnboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Set up workstation", null,
            OnboardingTemplateTaskAssignTo.Unassigned, null, FixedNow);
        var later = FixedNow.AddDays(1);

        task.Skip(later);

        Assert.Equal(OnboardingTaskStatus.Skipped, task.Status);
        Assert.Null(task.CompletedAt);
        Assert.Equal(later, task.UpdatedAt);
    }
}
