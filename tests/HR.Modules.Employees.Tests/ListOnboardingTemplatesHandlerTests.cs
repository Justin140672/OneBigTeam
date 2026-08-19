using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.ListOnboardingTemplates;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class ListOnboardingTemplatesHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Active_Templates_Ordered_By_Name_With_TaskCount()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var templateB = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "B Template", null, Now);
        templateB.AddTask(Guid.NewGuid(), "Task 1", null, TaskPriority.Medium, OnboardingTemplateTaskAssignTo.Unassigned, 0, 0, Now);
        templateB.AddTask(Guid.NewGuid(), "Task 2", null, TaskPriority.Medium, OnboardingTemplateTaskAssignTo.Unassigned, 0, 1, Now);

        var templateA = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "A Template", null, Now);

        var inactiveTemplate = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Inactive Template", null, Now);
        inactiveTemplate.Deactivate(Now);

        context.OnboardingTemplates.AddRange(templateB, templateA, inactiveTemplate);
        await context.SaveChangesAsync();

        var handler = new ListOnboardingTemplatesHandler(
            context,
            new HR.Modules.Employees.Services.OnboardingTemplateSeeder(context),
            new HR.Modules.Employees.Tests.Infrastructure.FakeClock(Now.UtcDateTime));
        var result = await handler.HandleAsync(
            new ListOnboardingTemplatesRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.Equal("A Template", result.Value.Items[0].Name);
        Assert.Equal("B Template", result.Value.Items[1].Name);
        Assert.Equal(2, result.Value.Items[1].TaskCount);
    }

    [Fact]
    public async Task HandleAsync_Includes_Inactive_Templates_When_Requested()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var inactiveTemplate = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Inactive Template", null, Now);
        inactiveTemplate.Deactivate(Now);
        context.OnboardingTemplates.Add(inactiveTemplate);
        await context.SaveChangesAsync();

        var handler = new ListOnboardingTemplatesHandler(
            context,
            new HR.Modules.Employees.Services.OnboardingTemplateSeeder(context),
            new HR.Modules.Employees.Tests.Infrastructure.FakeClock(Now.UtcDateTime));
        var result = await handler.HandleAsync(
            new ListOnboardingTemplatesRequest { CompanyId = companyId, IncludeInactive = true },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.False(result.Value.Items[0].IsActive);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Templates_From_Other_Companies()
    {
        await using var context = BuildContext();
        context.OnboardingTemplates.Add(
            OnboardingTemplate.Create(Guid.NewGuid(), Guid.NewGuid(), "Other Company Template", null, Now));
        await context.SaveChangesAsync();

        var handler = new ListOnboardingTemplatesHandler(
            context,
            new HR.Modules.Employees.Services.OnboardingTemplateSeeder(context),
            new HR.Modules.Employees.Tests.Infrastructure.FakeClock(Now.UtcDateTime));
        var result = await handler.HandleAsync(
            new ListOnboardingTemplatesRequest { CompanyId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        // The requesting company has no templates of its own yet, so OnboardingTemplateSeeder
        // lazily seeds the default "Standard Onboarding" template on this call — the other
        // company's template must never leak into this company's result regardless.
        Assert.Single(result.Value!.Items);
        Assert.Equal("Standard Onboarding", result.Value.Items[0].Name);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
