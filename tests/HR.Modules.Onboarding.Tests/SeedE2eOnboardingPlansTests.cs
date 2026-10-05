using HR.Infrastructure.Abstractions;
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
    private readonly FakeHrTaskCreator _hrTaskCreator = new();

    private static readonly Guid DefaultTemplateId = Guid.NewGuid();

    private static readonly IReadOnlyList<OnboardingTemplateTaskItem> TemplateTasks =
    [
        new(Guid.NewGuid(), "Prepare workstation", "Desk.", TaskPriority.High, OnboardingTemplateTaskAssignTo.Manager, 0, 1),
        new(Guid.NewGuid(), "Complete payroll details", "Payroll.", TaskPriority.High, OnboardingTemplateTaskAssignTo.NewHire, 3, 2),
        new(Guid.NewGuid(), "Right-to-work checks", "Verify.", TaskPriority.Critical, OnboardingTemplateTaskAssignTo.Hr, 1, 3),
    ];

    private ServiceProvider BuildProvider(string dbName, bool withDefaultTemplate = true)
    {
        var services = new ServiceCollection();
        services.AddDbContext<OnboardingDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<ITaskCreator>(_taskCreator);
        services.AddSingleton<IHrTaskCreator>(_hrTaskCreator);
        services.AddSingleton<IOnboardingTemplateReader>(new FakeOnboardingTemplateReader(
            defaultTemplateId: withDefaultTemplate ? DefaultTemplateId : null,
            tasksByTemplate: new Dictionary<Guid, IReadOnlyList<OnboardingTemplateTaskItem>> { [DefaultTemplateId] = TemplateTasks }));
        return services.BuildServiceProvider();
    }

    private static (Guid CompanyId, Guid EmployeeId, DateOnly StartDate, string EmployeeName) Emp(string name) =>
        (Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 3, 1), name);

    [Fact]
    public async Task Creates_NotStarted_Plan_From_Default_Template_Per_Employee()
    {
        var provider = BuildProvider(Guid.NewGuid().ToString("N"));
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
        Assert.Contains(tasksA, t => t.Title == "Prepare workstation — E2E SeedOnboardTabA" && t.DueDate == new DateOnly(2026, 3, 1));
        Assert.Contains(tasksA, t => t.Title == "Complete payroll details — E2E SeedOnboardTabA" && t.DueDate == new DateOnly(2026, 3, 4));
        Assert.Contains(tasksA, t => t.Title == "Right-to-work checks — E2E SeedOnboardTabA" && t.AssignTo == OnboardingTemplateTaskAssignTo.Hr);

        var createdForA = _taskCreator.Created.Where(t => t.Title.EndsWith("E2E SeedOnboardTabA")).ToList();
        Assert.Equal(2, createdForA.Count);
        Assert.All(createdForA, t => Assert.Equal(TaskSource.Onboarding, t.Source));
        Assert.Null(createdForA.Single(t => t.Title.StartsWith("Prepare workstation")).AssignedEmployeeId);
        Assert.Equal(a.EmployeeId, createdForA.Single(t => t.Title.StartsWith("Complete payroll")).AssignedEmployeeId);
        Assert.Single(_hrTaskCreator.Created, t => t.Title.EndsWith("E2E SeedOnboardTabA"));
    }

    [Fact]
    public async Task Creates_Empty_Plan_When_No_Default_Template()
    {
        var provider = BuildProvider(Guid.NewGuid().ToString("N"), withDefaultTemplate: false);

        await provider.SeedE2eOnboardingPlansAsync([Emp("E2E SeedOnboardTabA")]);

        await using var db = provider.GetRequiredService<OnboardingDbContext>();
        Assert.Equal(1, await db.OnboardingPlans.CountAsync());
        Assert.Equal(0, await db.OnboardingTasks.CountAsync());
    }

    [Fact]
    public async Task Is_Idempotent_Per_Employee()
    {
        var provider = BuildProvider(Guid.NewGuid().ToString("N"));
        var a = Emp("E2E SeedOnboardTabA");

        await provider.SeedE2eOnboardingPlansAsync([a]);
        await provider.SeedE2eOnboardingPlansAsync([a]);

        await using var db = provider.GetRequiredService<OnboardingDbContext>();
        Assert.Equal(1, await db.OnboardingPlans.CountAsync());
        Assert.Equal(3, await db.OnboardingTasks.CountAsync());
        Assert.Equal(2, _taskCreator.Created.Count);
        Assert.Single(_hrTaskCreator.Created);
    }
}
