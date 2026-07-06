using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.DeactivateOnboardingTemplate;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class DeactivateOnboardingTemplateHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Deactivates_Template()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var template = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Standard Onboarding", null, now);
        context.OnboardingTemplates.Add(template);
        await context.SaveChangesAsync();

        var handler = new DeactivateOnboardingTemplateHandler(context, new FakeClock(FixedUtcNow));
        var result = await handler.HandleAsync(
            new DeactivateOnboardingTemplateRequest { CompanyId = companyId, Id = template.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var saved = await context.OnboardingTemplates.SingleAsync();
        Assert.False(saved.IsActive);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Template_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new DeactivateOnboardingTemplateHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new DeactivateOnboardingTemplateRequest { CompanyId = Guid.NewGuid(), Id = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Already_Inactive()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var template = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Standard Onboarding", null, now);
        template.Deactivate(now);
        context.OnboardingTemplates.Add(template);
        await context.SaveChangesAsync();

        var handler = new DeactivateOnboardingTemplateHandler(context, new FakeClock(FixedUtcNow));
        var result = await handler.HandleAsync(
            new DeactivateOnboardingTemplateRequest { CompanyId = companyId, Id = template.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
