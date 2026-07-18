using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.RemoveOnboardingTemplateFromPositionProfile;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class RemoveOnboardingTemplateHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 27, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Deactivates_Active_Assignment()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var auditPublisher = new FakeAuditPublisher();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        context.PositionProfiles.Add(profile);

        var template = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Standard Onboarding", null, Now);
        context.OnboardingTemplates.Add(template);

        var assignment = PositionProfileOnboardingTemplate.Create(
            Guid.NewGuid(), companyId, profile.Id, template.Id, Guid.NewGuid(), Now);
        context.PositionProfileOnboardingTemplates.Add(assignment);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, auditPublisher);
        var result = await handler.HandleAsync(
            new RemoveOnboardingTemplateRequest(companyId, profile.Id, assignment.Id),
            actorId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var saved = await context.PositionProfileOnboardingTemplates.SingleAsync();
        Assert.False(saved.IsActive);

        Assert.Single(auditPublisher.Published);
        var evt = auditPublisher.Published[0];
        Assert.Equal("position-profile.onboarding-template.removed", evt.EventType);
        Assert.Equal(profile.Id, evt.EntityId);
        Assert.Equal(actorId, evt.ActorEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Assignment_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = BuildHandler(context, new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new RemoveOnboardingTemplateRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Already_Inactive()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        context.PositionProfiles.Add(profile);

        var template = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Standard Onboarding", null, Now);
        context.OnboardingTemplates.Add(template);

        var assignment = PositionProfileOnboardingTemplate.Create(
            Guid.NewGuid(), companyId, profile.Id, template.Id, Guid.NewGuid(), Now);
        assignment.Deactivate();
        context.PositionProfileOnboardingTemplates.Add(assignment);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeAuditPublisher());
        var result = await handler.HandleAsync(
            new RemoveOnboardingTemplateRequest(companyId, profile.Id, assignment.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Assignment_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        context.PositionProfiles.Add(profile);

        var template = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Standard Onboarding", null, Now);
        context.OnboardingTemplates.Add(template);

        var assignment = PositionProfileOnboardingTemplate.Create(
            Guid.NewGuid(), companyId, profile.Id, template.Id, Guid.NewGuid(), Now);
        context.PositionProfileOnboardingTemplates.Add(assignment);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeAuditPublisher());
        var result = await handler.HandleAsync(
            new RemoveOnboardingTemplateRequest(Guid.NewGuid(), profile.Id, assignment.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Assignment_Belongs_To_Different_Profile()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profileA = PositionProfile.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        var profileB = PositionProfile.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Manager", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        context.PositionProfiles.AddRange(profileA, profileB);

        var template = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Standard Onboarding", null, Now);
        context.OnboardingTemplates.Add(template);

        var assignment = PositionProfileOnboardingTemplate.Create(
            Guid.NewGuid(), companyId, profileA.Id, template.Id, Guid.NewGuid(), Now);
        context.PositionProfileOnboardingTemplates.Add(assignment);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeAuditPublisher());
        var result = await handler.HandleAsync(
            new RemoveOnboardingTemplateRequest(companyId, profileB.Id, assignment.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static RemoveOnboardingTemplateHandler BuildHandler(
        EmployeesDbContext context,
        FakeAuditPublisher auditPublisher)
        => new(context, new FakeClock(FixedUtcNow), auditPublisher);

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
