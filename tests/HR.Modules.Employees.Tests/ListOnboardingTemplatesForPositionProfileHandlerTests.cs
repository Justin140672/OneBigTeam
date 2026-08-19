using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.ListOnboardingTemplatesForPositionProfile;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class ListOnboardingTemplatesForPositionProfileHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 27, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Active_Assignments_With_Names_And_TaskCount()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        context.PositionProfiles.Add(profile);

        var template = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Standard Onboarding", "Default checklist", Now);
        template.AddTask(Guid.NewGuid(), "Send welcome email", null, TaskPriority.Medium, OnboardingTemplateTaskAssignTo.NewHire, 1, 1, Now);
        template.AddTask(Guid.NewGuid(), "Order equipment", null, TaskPriority.High, OnboardingTemplateTaskAssignTo.Manager, 2, 2, Now);
        context.OnboardingTemplates.Add(template);

        var assignment = PositionProfileOnboardingTemplate.Create(
            Guid.NewGuid(), companyId, profile.Id, template.Id, Guid.NewGuid(), Now);
        context.PositionProfileOnboardingTemplates.Add(assignment);
        await context.SaveChangesAsync();

        var handler = new ListOnboardingTemplatesForPositionProfileHandler(context);
        var result = await handler.HandleAsync(
            new ListOnboardingTemplatesForPositionProfileRequest(companyId, profile.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        var item = result.Value.Items[0];
        Assert.Equal(assignment.Id, item.Id);
        Assert.Equal(template.Id, item.OnboardingTemplateId);
        Assert.Equal("Standard Onboarding", item.Name);
        Assert.Equal("Default checklist", item.Description);
        Assert.Equal(2, item.TaskCount);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Inactive_Tasks_From_TaskCount()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        context.PositionProfiles.Add(profile);

        var template = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Standard Onboarding", null, Now);
        template.AddTask(Guid.NewGuid(), "Send welcome email", null, TaskPriority.Medium, OnboardingTemplateTaskAssignTo.NewHire, 1, 1, Now);
        var taskToRemove = template.AddTask(Guid.NewGuid(), "Order equipment", null, TaskPriority.High, OnboardingTemplateTaskAssignTo.Manager, 2, 2, Now);
        template.RemoveTask(taskToRemove.Id, Now);
        context.OnboardingTemplates.Add(template);

        var assignment = PositionProfileOnboardingTemplate.Create(
            Guid.NewGuid(), companyId, profile.Id, template.Id, Guid.NewGuid(), Now);
        context.PositionProfileOnboardingTemplates.Add(assignment);
        await context.SaveChangesAsync();

        var handler = new ListOnboardingTemplatesForPositionProfileHandler(context);
        var result = await handler.HandleAsync(
            new ListOnboardingTemplatesForPositionProfileRequest(companyId, profile.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal(1, result.Value.Items[0].TaskCount);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Removed_Assignments()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        context.PositionProfiles.Add(profile);

        var template = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Standard Onboarding", null, Now);
        context.OnboardingTemplates.Add(template);

        var removed = PositionProfileOnboardingTemplate.Create(
            Guid.NewGuid(), companyId, profile.Id, template.Id, Guid.NewGuid(), Now);
        removed.Deactivate();
        context.PositionProfileOnboardingTemplates.Add(removed);
        await context.SaveChangesAsync();

        var handler = new ListOnboardingTemplatesForPositionProfileHandler(context);
        var result = await handler.HandleAsync(
            new ListOnboardingTemplatesForPositionProfileRequest(companyId, profile.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_When_No_Assignments()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var handler = new ListOnboardingTemplatesForPositionProfileHandler(context);
        var result = await handler.HandleAsync(
            new ListOnboardingTemplatesForPositionProfileRequest(companyId, profile.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Unknown_PositionProfile()
    {
        await using var context = BuildContext();

        var handler = new ListOnboardingTemplatesForPositionProfileHandler(context);
        var result = await handler.HandleAsync(
            new ListOnboardingTemplatesForPositionProfileRequest(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Isolates_Results_By_Company()
    {
        await using var context = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        var profileA = PositionProfile.Create(Guid.NewGuid(), companyA, Guid.NewGuid(), Guid.NewGuid(), "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        var profileB = PositionProfile.Create(Guid.NewGuid(), companyB, Guid.NewGuid(), Guid.NewGuid(), "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        context.PositionProfiles.AddRange(profileA, profileB);

        var templateA = OnboardingTemplate.Create(Guid.NewGuid(), companyA, "Template A", null, Now);
        var templateB = OnboardingTemplate.Create(Guid.NewGuid(), companyB, "Template B", null, Now);
        context.OnboardingTemplates.AddRange(templateA, templateB);

        var assignmentA = PositionProfileOnboardingTemplate.Create(
            Guid.NewGuid(), companyA, profileA.Id, templateA.Id, Guid.NewGuid(), Now);
        var assignmentB = PositionProfileOnboardingTemplate.Create(
            Guid.NewGuid(), companyB, profileB.Id, templateB.Id, Guid.NewGuid(), Now);
        context.PositionProfileOnboardingTemplates.AddRange(assignmentA, assignmentB);
        await context.SaveChangesAsync();

        var handler = new ListOnboardingTemplatesForPositionProfileHandler(context);
        var result = await handler.HandleAsync(
            new ListOnboardingTemplatesForPositionProfileRequest(companyA, profileA.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal(assignmentA.Id, result.Value.Items[0].Id);
    }

    [Fact]
    public async Task HandleAsync_Isolates_Results_By_PositionProfile()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profileA = PositionProfile.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        var profileB = PositionProfile.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Manager", null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        context.PositionProfiles.AddRange(profileA, profileB);

        var template = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Standard Onboarding", null, Now);
        context.OnboardingTemplates.Add(template);

        var assignmentA = PositionProfileOnboardingTemplate.Create(
            Guid.NewGuid(), companyId, profileA.Id, template.Id, Guid.NewGuid(), Now);
        var assignmentB = PositionProfileOnboardingTemplate.Create(
            Guid.NewGuid(), companyId, profileB.Id, template.Id, Guid.NewGuid(), Now);
        context.PositionProfileOnboardingTemplates.AddRange(assignmentA, assignmentB);
        await context.SaveChangesAsync();

        var handler = new ListOnboardingTemplatesForPositionProfileHandler(context);
        var result = await handler.HandleAsync(
            new ListOnboardingTemplatesForPositionProfileRequest(companyId, profileA.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal(assignmentA.Id, result.Value.Items[0].Id);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
