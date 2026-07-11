using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Offboarding.Tests;

public class OffboardingDbContextTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 25, 10, 0, 0, TimeSpan.Zero);

    private static OffboardingDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<OffboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task Saves_And_Retrieves_OffboardingPlan()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), companyId, employeeId, new DateOnly(2026, 7, 1), "Notice given.", FixedNow);

        context.OffboardingPlans.Add(plan);
        await context.SaveChangesAsync();

        var saved = await context.OffboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(companyId, saved.CompanyId);
        Assert.Equal(employeeId, saved.EmployeeId);
        Assert.Equal(OffboardingStatus.NotStarted, saved.Status);
        Assert.Equal("Notice given.", saved.Notes);
    }

    [Fact]
    public async Task Persists_OffboardingPlan_Status_Transitions()
    {
        await using var context = BuildContext();
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), null, FixedNow);
        context.OffboardingPlans.Add(plan);
        await context.SaveChangesAsync();

        plan.Start(FixedNow.AddDays(1));
        await context.SaveChangesAsync();

        var reloaded = await context.OffboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(OffboardingStatus.InProgress, reloaded.Status);
    }

    [Fact]
    public async Task Saves_And_Retrieves_OffboardingTask()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var task = OffboardingTask.Create(
            Guid.NewGuid(), companyId, planId, "Return laptop", "Return before last day.",
            OffboardingTaskAssignTo.Employee, new DateOnly(2026, 7, 1), FixedNow);

        context.OffboardingTasks.Add(task);
        await context.SaveChangesAsync();

        var saved = await context.OffboardingTasks.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(companyId, saved.CompanyId);
        Assert.Equal(planId, saved.OffboardingPlanId);
        Assert.Equal("Return laptop", saved.Title);
        Assert.Equal(OffboardingTaskAssignTo.Employee, saved.AssignTo);
        Assert.Equal(OffboardingTaskStatus.Pending, saved.Status);
    }

    [Fact]
    public async Task Persists_OffboardingTask_CompletedAt_When_Completed()
    {
        await using var context = BuildContext();
        var task = OffboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Return laptop", null,
            OffboardingTaskAssignTo.Employee, null, FixedNow);
        context.OffboardingTasks.Add(task);
        await context.SaveChangesAsync();

        var completedAt = FixedNow.AddDays(1);
        task.Complete(completedAt);
        await context.SaveChangesAsync();

        var reloaded = await context.OffboardingTasks.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(OffboardingTaskStatus.Completed, reloaded.Status);
        Assert.Equal(completedAt, reloaded.CompletedAt);
    }

    [Fact]
    public void Model_Uses_Offboarding_Schema()
    {
        using var context = BuildContext();

        Assert.Equal("offboarding", context.Model.GetDefaultSchema());
    }
}
