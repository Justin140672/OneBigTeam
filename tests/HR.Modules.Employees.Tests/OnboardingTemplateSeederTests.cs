using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class OnboardingTemplateSeederTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EnsureDefaultTemplateSeededAsync_Creates_Standard_Onboarding_Template_With_Tasks_For_Fresh_Company()
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
        Assert.NotEmpty(template.Tasks);
        Assert.Equal(7, template.Tasks.Count);
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
