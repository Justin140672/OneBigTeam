using HR.Modules.Onboarding.Domain;
using HR.Modules.Onboarding.Persistence;
using HR.Modules.Onboarding.Tests.Infrastructure;
using HR.Modules.Tasks.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Onboarding.Tests;

public class SeedE2eOnboardingPlansTests
{
    private readonly FakeTaskCreator _taskCreator = new();

    private ServiceProvider BuildProvider(string dbName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<OnboardingDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<ITaskCreator>(_taskCreator);
        return services.BuildServiceProvider();
    }

    private static (Guid CompanyId, Guid EmployeeId, DateOnly StartDate, string EmployeeName) Emp(string name) =>
        (Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 3, 1), name);

    [Fact]
    public async Task Creates_NotStarted_Plan_With_Three_Default_Tasks_Per_Employee()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var provider = BuildProvider(dbName);
        var a = Emp("E2E SeedOnboardTabA");
        var b = Emp("E2E SeedOnboardTabB");

        await provider.SeedE2eOnboardingPlansAsync([a, b]);

        await using var db = provider.GetRequiredService<OnboardingDbContext>();
        var plans = await db.OnboardingPlans.ToListAsync();
        Assert.Equal(2, plans.Count);
        Assert.All(plans, p => Assert.Equal(OnboardingStatus.NotStarted, p.Status));

        var planA = plans.Single(p => p.EmployeeId == a.EmployeeId);
        var tasksA = await db.OnboardingTasks.Where(t => t.OnboardingPlanId == planA.Id).ToListAsync();
        Assert.Equal(3, tasksA.Count);
        Assert.All(tasksA, t => Assert.Equal(OnboardingTaskStatus.Pending, t.Status));
        Assert.Contains(tasksA, t => t.Title == "Set up workstation and system access — E2E SeedOnboardTabA");
        Assert.Contains(tasksA, t => t.Title == "Send welcome email and first-day details — E2E SeedOnboardTabA");
        Assert.Contains(tasksA, t => t.Title == "Schedule welcome and induction meeting — E2E SeedOnboardTabA");
        Assert.Contains(tasksA, t => t.DueDate == new DateOnly(2026, 3, 8));

        // Matching unassigned Tasks-module tasks were also created (so they surface in the HR Inbox).
        var createdForA = _taskCreator.Created.Where(t => t.Title.EndsWith("E2E SeedOnboardTabA")).ToList();
        Assert.Equal(3, createdForA.Count);
        Assert.All(createdForA, t =>
        {
            Assert.Equal(TaskSource.Onboarding, t.Source);
            Assert.Null(t.AssignedEmployeeId);
            Assert.Null(t.AssignedUserId);
        });
    }

    [Fact]
    public async Task Is_Idempotent_Per_Employee()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var provider = BuildProvider(dbName);
        var a = Emp("E2E SeedOnboardTabA");

        await provider.SeedE2eOnboardingPlansAsync([a]);
        await provider.SeedE2eOnboardingPlansAsync([a]);

        await using var db = provider.GetRequiredService<OnboardingDbContext>();
        Assert.Equal(1, await db.OnboardingPlans.CountAsync());
        Assert.Equal(3, await db.OnboardingTasks.CountAsync());
        Assert.Equal(3, _taskCreator.Created.Count);
    }
}
