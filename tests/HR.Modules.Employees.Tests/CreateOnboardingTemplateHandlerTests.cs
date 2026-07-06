using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreateOnboardingTemplate;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class CreateOnboardingTemplateHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Creates_OnboardingTemplate()
    {
        await using var context = BuildContext();
        var handler = new CreateOnboardingTemplateHandler(context, new FakeClock(FixedUtcNow));
        var companyId = Guid.NewGuid();

        var result = await handler.HandleAsync(
            new CreateOnboardingTemplateRequest
            {
                CompanyId = companyId,
                Name = "Standard Engineering Onboarding",
                Description = "Default checklist for engineering hires",
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(companyId, result.Value!.CompanyId);
        Assert.Equal("Standard Engineering Onboarding", result.Value.Name);
        Assert.True(result.Value.IsActive);

        var saved = await context.OnboardingTemplates.SingleAsync();
        Assert.Equal(result.Value.Id, saved.Id);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Name_Already_Exists()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        context.OnboardingTemplates.Add(
            OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Standard Onboarding", null, now));
        await context.SaveChangesAsync();

        var handler = new CreateOnboardingTemplateHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreateOnboardingTemplateRequest { CompanyId = companyId, Name = "Standard Onboarding" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Same_Name_In_Different_Companies()
    {
        await using var context = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        context.OnboardingTemplates.Add(
            OnboardingTemplate.Create(Guid.NewGuid(), companyA, "Standard Onboarding", null, now));
        await context.SaveChangesAsync();

        var handler = new CreateOnboardingTemplateHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreateOnboardingTemplateRequest { CompanyId = companyB, Name = "Standard Onboarding" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Allows_Reusing_Name_Of_Deactivated_Template()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var existing = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Standard Onboarding", null, now);
        existing.Deactivate(now);
        context.OnboardingTemplates.Add(existing);
        await context.SaveChangesAsync();

        var handler = new CreateOnboardingTemplateHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreateOnboardingTemplateRequest { CompanyId = companyId, Name = "Standard Onboarding" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
