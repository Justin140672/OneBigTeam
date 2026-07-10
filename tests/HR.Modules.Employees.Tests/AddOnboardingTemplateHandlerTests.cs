using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.AddOnboardingTemplateToPositionProfile;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class AddOnboardingTemplateHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 27, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Adds_OnboardingTemplate_To_PositionProfile()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var auditPublisher = new FakeAuditPublisher();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, null, "Engineer", null, null, null, null, null, null, null, null, Now);
        context.PositionProfiles.Add(profile);

        var template = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Standard Onboarding", null, Now);
        context.OnboardingTemplates.Add(template);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, auditPublisher);

        var result = await handler.HandleAsync(
            new AddOnboardingTemplateRequest(companyId, profile.Id, template.Id),
            actorId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(profile.Id, result.Value!.PositionProfileId);
        Assert.Equal(template.Id, result.Value.OnboardingTemplateId);

        var saved = await context.PositionProfileOnboardingTemplates.SingleAsync();
        Assert.Equal(profile.Id, saved.PositionProfileId);
        Assert.Equal(template.Id, saved.OnboardingTemplateId);
        Assert.Equal(companyId, saved.CompanyId);
        Assert.True(saved.IsActive);
        Assert.Equal(actorId, saved.CreatedBy);
        Assert.Equal(saved.Id, result.Value.Id);

        Assert.Single(auditPublisher.Published);
        var auditEvent = auditPublisher.Published[0];
        Assert.Equal("position-profile.onboarding-template.assigned", auditEvent.EventType);
        Assert.Equal(profile.Id, auditEvent.EntityId);
        Assert.Equal(actorId, auditEvent.ActorEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Profile_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = BuildHandler(context, new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new AddOnboardingTemplateRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Profile_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var profile = PositionProfile.Create(Guid.NewGuid(), Guid.NewGuid(), null, null, "Engineer", null, null, null, null, null, null, null, null, Now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new AddOnboardingTemplateRequest(Guid.NewGuid(), profile.Id, Guid.NewGuid()),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_OnboardingTemplate_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, null, "Engineer", null, null, null, null, null, null, null, null, Now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new AddOnboardingTemplateRequest(companyId, profile.Id, Guid.NewGuid()),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_OnboardingTemplate_Is_Inactive()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, null, "Engineer", null, null, null, null, null, null, null, null, Now);
        context.PositionProfiles.Add(profile);

        var template = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Standard Onboarding", null, Now);
        template.Deactivate(Now);
        context.OnboardingTemplates.Add(template);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new AddOnboardingTemplateRequest(companyId, profile.Id, template.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_OnboardingTemplate_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, null, "Engineer", null, null, null, null, null, null, null, null, Now);
        context.PositionProfiles.Add(profile);

        var template = OnboardingTemplate.Create(Guid.NewGuid(), Guid.NewGuid(), "Standard Onboarding", null, Now);
        context.OnboardingTemplates.Add(template);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new AddOnboardingTemplateRequest(companyId, profile.Id, template.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Template_Already_Assigned()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, null, "Engineer", null, null, null, null, null, null, null, null, Now);
        context.PositionProfiles.Add(profile);

        var template = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Standard Onboarding", null, Now);
        context.OnboardingTemplates.Add(template);

        var existing = PositionProfileOnboardingTemplate.Create(
            Guid.NewGuid(), companyId, profile.Id, template.Id, Guid.NewGuid(), Now);
        context.PositionProfileOnboardingTemplates.Add(existing);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new AddOnboardingTemplateRequest(companyId, profile.Id, template.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Reassignment_After_Previous_Assignment_Was_Removed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, null, "Engineer", null, null, null, null, null, null, null, null, Now);
        context.PositionProfiles.Add(profile);

        var template = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Standard Onboarding", null, Now);
        context.OnboardingTemplates.Add(template);

        var removed = PositionProfileOnboardingTemplate.Create(
            Guid.NewGuid(), companyId, profile.Id, template.Id, Guid.NewGuid(), Now);
        removed.Deactivate();
        context.PositionProfileOnboardingTemplates.Add(removed);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new AddOnboardingTemplateRequest(companyId, profile.Id, template.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Allows_Same_Template_On_Different_Profile()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profileA = PositionProfile.Create(Guid.NewGuid(), companyId, null, null, "Engineer", null, null, null, null, null, null, null, null, Now);
        var profileB = PositionProfile.Create(Guid.NewGuid(), companyId, null, null, "Manager", null, null, null, null, null, null, null, null, Now);
        context.PositionProfiles.AddRange(profileA, profileB);

        var template = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Standard Onboarding", null, Now);
        context.OnboardingTemplates.Add(template);

        var existing = PositionProfileOnboardingTemplate.Create(
            Guid.NewGuid(), companyId, profileA.Id, template.Id, Guid.NewGuid(), Now);
        context.PositionProfileOnboardingTemplates.Add(existing);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeAuditPublisher());

        var result = await handler.HandleAsync(
            new AddOnboardingTemplateRequest(companyId, profileB.Id, template.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    private static AddOnboardingTemplateHandler BuildHandler(
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
