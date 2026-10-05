using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Migrations;
using HR.Modules.Employees.Persistence;
using HR.Modules.Tasks.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UpgradeSeededStandardOnboardingTemplateMigrationTests(ApiWebApplicationFactory factory)
{
    private const string LegacyDescription =
        "Default new-starter checklist covering the essentials for a new hire's first two weeks.";

    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    private static readonly (string Title, string Description, TaskPriority Priority, OnboardingTemplateTaskAssignTo Owner, int Due, int Order)[] LegacyTasks =
    [
        ("Send welcome email", "Introduce the company, share first-day logistics.", TaskPriority.High, OnboardingTemplateTaskAssignTo.Manager, 0, 1),
        ("Prepare workstation and equipment", "Laptop, accounts, desk setup ready before day one.", TaskPriority.High, OnboardingTemplateTaskAssignTo.Manager, 0, 2),
        ("Complete right-to-work checks", "Verify and file required employment documentation.", TaskPriority.Critical, OnboardingTemplateTaskAssignTo.Manager, 1, 3),
        ("Company induction session", "Overview of policies, values, and company structure.", TaskPriority.Medium, OnboardingTemplateTaskAssignTo.NewHire, 3, 4),
        ("Meet the team", "Introductions with immediate team and key stakeholders.", TaskPriority.Medium, OnboardingTemplateTaskAssignTo.Manager, 3, 5),
        ("Set 30-day goals", "Agree initial objectives and success measures.", TaskPriority.Medium, OnboardingTemplateTaskAssignTo.Manager, 14, 6),
        ("Complete mandatory training", "Health & safety, compliance and any role-specific training.", TaskPriority.High, OnboardingTemplateTaskAssignTo.NewHire, 14, 7),
    ];

    private static OnboardingTemplate BuildLegacyTemplate(Guid companyId, string name = "Standard Onboarding", string description = LegacyDescription)
    {
        var template = OnboardingTemplate.Create(Guid.NewGuid(), companyId, name, description, Now, isDefault: true);
        foreach (var t in LegacyTasks)
            template.AddTask(Guid.NewGuid(), t.Title, t.Description, t.Priority, t.Owner, t.Due, t.Order, Now);
        return template;
    }

    private async Task<OnboardingTemplate> LoadAsync(Guid companyId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        return await db.OnboardingTemplates.AsNoTracking().Include(t => t.Tasks).SingleAsync(t => t.CompanyId == companyId);
    }

    private async Task RunMigrationAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var migration = new UpgradeSeededStandardOnboardingTemplate();
        foreach (var operation in migration.UpOperations.OfType<SqlOperation>())
            await db.Database.ExecuteSqlRawAsync(operation.Sql);
    }

    [Fact]
    public async Task Upgrades_Unmodified_Seeded_Template_And_Leaves_Customised_Templates_Untouched()
    {
        var unmodifiedCompany = Guid.NewGuid();
        var editedTaskCompany = Guid.NewGuid();
        var editedDescriptionCompany = Guid.NewGuid();
        var removedTaskCompany = Guid.NewGuid();
        var inactiveCompany = Guid.NewGuid();
        var renamedCompany = Guid.NewGuid();

        var editedTask = BuildLegacyTemplate(editedTaskCompany);
        editedTask.ReplaceTasks(
            editedTask.Tasks.Select(t => (
                (Guid?)t.Id, t.Title, t.Description, t.Priority, t.AssignTo,
                t.DisplayOrder == 2 ? 5 : t.DueDaysAfterStart, t.DisplayOrder)).ToList(),
            Now);

        var removedTask = BuildLegacyTemplate(removedTaskCompany);
        removedTask.RemoveTask(removedTask.Tasks.First().Id, Now);

        var inactive = BuildLegacyTemplate(inactiveCompany);
        inactive.UnmarkAsDefault(Now);
        inactive.Deactivate(Now);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            db.OnboardingTemplates.AddRange(
                BuildLegacyTemplate(unmodifiedCompany),
                editedTask,
                BuildLegacyTemplate(editedDescriptionCompany, description: "Our own description."),
                removedTask,
                inactive,
                BuildLegacyTemplate(renamedCompany, name: "Our Onboarding"));
            await db.SaveChangesAsync();
        }

        var versionBefore = (await LoadAsync(unmodifiedCompany)).Version;

        await RunMigrationAsync();

        var upgraded = await LoadAsync(unmodifiedCompany);
        Assert.Equal(15, upgraded.Tasks.Count);
        Assert.All(upgraded.Tasks, t => Assert.True(t.IsActive));
        Assert.Equal(Enumerable.Range(1, 15), upgraded.Tasks.Select(t => t.DisplayOrder).OrderBy(o => o));
        Assert.Contains(upgraded.Tasks, t =>
            t.Title == "Complete right-to-work and employment-document checks"
            && t.AssignTo == OnboardingTemplateTaskAssignTo.Hr
            && t.Priority == TaskPriority.Critical);
        Assert.Single(upgraded.Tasks, t => t.AssignTo == OnboardingTemplateTaskAssignTo.Hr);
        Assert.DoesNotContain(upgraded.Tasks, t => t.Title == "Send welcome email");
        Assert.True(upgraded.IsDefault);
        Assert.True(upgraded.Version > versionBefore);
        Assert.Contains("first 30 days", upgraded.Description);

        foreach (var untouchedCompany in new[] { editedTaskCompany, editedDescriptionCompany, removedTaskCompany, inactiveCompany, renamedCompany })
        {
            var template = await LoadAsync(untouchedCompany);
            Assert.Equal(7, template.Tasks.Count);
            Assert.DoesNotContain(template.Tasks, t => t.AssignTo == OnboardingTemplateTaskAssignTo.Hr);
        }

        Assert.Equal(6, (await LoadAsync(removedTaskCompany)).Tasks.Count(t => t.IsActive));
        Assert.Equal(5, (await LoadAsync(editedTaskCompany)).Tasks.Single(t => t.DisplayOrder == 2).DueDaysAfterStart);
        Assert.Equal("Our own description.", (await LoadAsync(editedDescriptionCompany)).Description);
    }

    [Fact]
    public async Task Rerunning_The_Upgrade_Is_Idempotent()
    {
        var companyId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            db.OnboardingTemplates.Add(BuildLegacyTemplate(companyId));
            await db.SaveChangesAsync();
        }

        await RunMigrationAsync();
        var afterFirst = await LoadAsync(companyId);
        await RunMigrationAsync();
        var afterSecond = await LoadAsync(companyId);

        Assert.Equal(15, afterSecond.Tasks.Count);
        Assert.Equal(afterFirst.Version, afterSecond.Version);
        Assert.Equal(
            afterFirst.Tasks.Select(t => t.Id).OrderBy(i => i),
            afterSecond.Tasks.Select(t => t.Id).OrderBy(i => i));
    }
}
