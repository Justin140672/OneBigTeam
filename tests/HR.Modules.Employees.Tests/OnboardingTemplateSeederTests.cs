using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class OnboardingTemplateSeederTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EnsureDefaultTemplateSeededAsync_Creates_Expanded_Standard_Onboarding_Template_For_Fresh_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var seeder = new OnboardingTemplateSeeder(context);

        await seeder.EnsureDefaultTemplateSeededAsync(companyId, Now, CancellationToken.None);

        var templates = await context.OnboardingTemplates
            .Where(t => t.CompanyId == companyId)
            .ToListAsync();

        var template = Assert.Single(templates);
        Assert.Equal("Standard Onboarding", template.Name);
        Assert.True(template.IsDefault);
        Assert.True(template.IsActive);
        Assert.Equal(15, template.Tasks.Count);
        Assert.All(template.Tasks, t => Assert.True(t.IsActive));
    }

    [Fact]
    public async Task EnsureDefaultTemplateSeededAsync_Seeds_Expected_Titles_Owners_And_Order()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        await new OnboardingTemplateSeeder(context).EnsureDefaultTemplateSeededAsync(companyId, Now, CancellationToken.None);

        var tasks = (await context.OnboardingTemplates.Include(t => t.Tasks).SingleAsync(t => t.CompanyId == companyId))
            .Tasks.OrderBy(t => t.DisplayOrder).ToList();

        var expected = new (string Title, OnboardingTemplateTaskAssignTo Owner)[]
        {
            ("Send welcome email and first-day information", OnboardingTemplateTaskAssignTo.Manager),
            ("Prepare workstation, equipment, accounts, and system access", OnboardingTemplateTaskAssignTo.Manager),
            ("Complete personal and emergency-contact details", OnboardingTemplateTaskAssignTo.NewHire),
            ("Complete payroll and tax information", OnboardingTemplateTaskAssignTo.NewHire),
            ("Complete right-to-work and employment-document checks", OnboardingTemplateTaskAssignTo.Hr),
            ("Review company policies and required acknowledgements", OnboardingTemplateTaskAssignTo.NewHire),
            ("Attend company induction", OnboardingTemplateTaskAssignTo.NewHire),
            ("Complete role-specific induction", OnboardingTemplateTaskAssignTo.Manager),
            ("Meet the team and key stakeholders", OnboardingTemplateTaskAssignTo.Manager),
            ("Complete security and data-protection training", OnboardingTemplateTaskAssignTo.NewHire),
            ("Complete health-and-safety and mandatory training", OnboardingTemplateTaskAssignTo.NewHire),
            ("Hold first-week check-in", OnboardingTemplateTaskAssignTo.Manager),
            ("Agree initial objectives and 30-day goals", OnboardingTemplateTaskAssignTo.Manager),
            ("Hold 30-day review and collect feedback", OnboardingTemplateTaskAssignTo.Manager),
            ("Confirm probation expectations and review schedule", OnboardingTemplateTaskAssignTo.Manager),
        };

        Assert.Equal(expected.Length, tasks.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Title, tasks[i].Title);
            Assert.Equal(expected[i].Owner, tasks[i].AssignTo);
            Assert.Equal(i + 1, tasks[i].DisplayOrder);
            Assert.False(string.IsNullOrWhiteSpace(tasks[i].Description));
            Assert.True(tasks[i].DueDaysAfterStart >= 0);
        }

        Assert.Equal(1, tasks.Count(t => t.AssignTo == OnboardingTemplateTaskAssignTo.Hr));
        Assert.DoesNotContain(tasks, t => t.AssignTo == OnboardingTemplateTaskAssignTo.Unassigned);
    }

    [Fact]
    public async Task EnsureDefaultTemplateSeededAsync_Is_NoOp_When_Called_Again()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var seeder = new OnboardingTemplateSeeder(context);

        await seeder.EnsureDefaultTemplateSeededAsync(companyId, Now, CancellationToken.None);
        await seeder.EnsureDefaultTemplateSeededAsync(companyId, Now, CancellationToken.None);

        var templates = await context.OnboardingTemplates
            .Where(t => t.CompanyId == companyId)
            .ToListAsync();

        Assert.Single(templates);
    }

    [Fact]
    public async Task EnsureDefaultTemplateSeededAsync_Is_NoOp_When_Company_Already_Has_A_Different_Template()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var existingTemplate = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Engineering Onboarding", null, Now);
        context.OnboardingTemplates.Add(existingTemplate);
        await context.SaveChangesAsync();

        var seeder = new OnboardingTemplateSeeder(context);
        await seeder.EnsureDefaultTemplateSeededAsync(companyId, Now, CancellationToken.None);

        var templates = await context.OnboardingTemplates
            .Where(t => t.CompanyId == companyId)
            .ToListAsync();

        var template = Assert.Single(templates);
        Assert.Equal("Engineering Onboarding", template.Name);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
