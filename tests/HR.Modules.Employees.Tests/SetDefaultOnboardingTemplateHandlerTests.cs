using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.SetDefaultOnboardingTemplate;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class SetDefaultOnboardingTemplateHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Swaps_Default_From_TemplateA_To_TemplateB()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var templateA = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Template A", null, now, isDefault: true);
        var templateB = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Template B", null, now);
        context.OnboardingTemplates.AddRange(templateA, templateB);
        await context.SaveChangesAsync();

        var handler = new SetDefaultOnboardingTemplateHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new SetDefaultOnboardingTemplateRequest { CompanyId = companyId, Id = templateB.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False((await context.OnboardingTemplates.SingleAsync(t => t.Id == templateA.Id)).IsDefault);
        Assert.True((await context.OnboardingTemplates.SingleAsync(t => t.Id == templateB.Id)).IsDefault);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Touch_Defaults_Of_Other_Companies()
    {
        await using var context = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var otherDefault = OnboardingTemplate.Create(Guid.NewGuid(), companyB, "Other", null, now, isDefault: true);
        var target = OnboardingTemplate.Create(Guid.NewGuid(), companyA, "Target", null, now);
        context.OnboardingTemplates.AddRange(otherDefault, target);
        await context.SaveChangesAsync();

        var handler = new SetDefaultOnboardingTemplateHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new SetDefaultOnboardingTemplateRequest { CompanyId = companyA, Id = target.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True((await context.OnboardingTemplates.SingleAsync(t => t.Id == otherDefault.Id)).IsDefault);
        Assert.True((await context.OnboardingTemplates.SingleAsync(t => t.Id == target.Id)).IsDefault);
    }

    [Fact]
    public async Task HandleAsync_Is_NoOp_Success_When_Template_Is_Already_Default()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var template = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Template", null, now, isDefault: true);
        context.OnboardingTemplates.Add(template);
        await context.SaveChangesAsync();

        var handler = new SetDefaultOnboardingTemplateHandler(context, new FakeClock(FixedUtcNow.AddHours(1)));

        var result = await handler.HandleAsync(
            new SetDefaultOnboardingTemplateRequest { CompanyId = companyId, Id = template.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var reloaded = await context.OnboardingTemplates.SingleAsync(t => t.Id == template.Id);
        Assert.True(reloaded.IsDefault);
        Assert.Equal(now, reloaded.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Failure_When_Template_Is_Inactive()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var template = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Template", null, now);
        template.Deactivate(now);
        context.OnboardingTemplates.Add(template);
        await context.SaveChangesAsync();

        var handler = new SetDefaultOnboardingTemplateHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new SetDefaultOnboardingTemplateRequest { CompanyId = companyId, Id = template.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.False((await context.OnboardingTemplates.SingleAsync(t => t.Id == template.Id)).IsDefault);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Template_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new SetDefaultOnboardingTemplateHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new SetDefaultOnboardingTemplateRequest { CompanyId = Guid.NewGuid(), Id = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Template_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var template = OnboardingTemplate.Create(Guid.NewGuid(), Guid.NewGuid(), "Template", null, now);
        context.OnboardingTemplates.Add(template);
        await context.SaveChangesAsync();

        var handler = new SetDefaultOnboardingTemplateHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new SetDefaultOnboardingTemplateRequest { CompanyId = Guid.NewGuid(), Id = template.Id },
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
