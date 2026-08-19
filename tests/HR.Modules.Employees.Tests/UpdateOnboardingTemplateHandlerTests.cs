using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.UpdateOnboardingTemplate;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class UpdateOnboardingTemplateHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Updates_Name_And_Description()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var template = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Old Name", "Old description", now);
        context.OnboardingTemplates.Add(template);
        await context.SaveChangesAsync();

        var handler = new UpdateOnboardingTemplateHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new UpdateOnboardingTemplateRequest
            {
                CompanyId = companyId,
                Id = template.Id,
                Name = "New Name",
                Description = "New description",
                Tasks = [],
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Name", result.Value!.Name);
        Assert.Equal("New description", result.Value.Description);
    }

    [Fact]
    public async Task HandleAsync_Adds_New_Tasks()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var template = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Template", null, now);
        context.OnboardingTemplates.Add(template);
        await context.SaveChangesAsync();

        var handler = new UpdateOnboardingTemplateHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new UpdateOnboardingTemplateRequest
            {
                CompanyId = companyId,
                Id = template.Id,
                Name = "Template",
                Tasks =
                [
                    new UpdateOnboardingTemplateTaskItem(null, "Set up laptop", null, TaskPriority.High, OnboardingTemplateTaskAssignTo.Unassigned, 0, 0),
                    new UpdateOnboardingTemplateTaskItem(null, "Welcome email", null, TaskPriority.Medium, OnboardingTemplateTaskAssignTo.Manager, 0, 1),
                ],
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Tasks.Count);
        Assert.Equal("Set up laptop", result.Value.Tasks[0].Title);
        Assert.Equal("Welcome email", result.Value.Tasks[1].Title);

        var saved = await context.OnboardingTemplates.Include(t => t.Tasks).SingleAsync();
        Assert.Equal(2, saved.Tasks.Count(t => t.IsActive));
    }

    [Fact]
    public async Task HandleAsync_Updates_Existing_Task_In_Place()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var template = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Template", null, now);
        var task = template.AddTask(Guid.NewGuid(), "Old Title", null, TaskPriority.Low, OnboardingTemplateTaskAssignTo.Unassigned, 0, 0, now);
        context.OnboardingTemplates.Add(template);
        await context.SaveChangesAsync();

        var handler = new UpdateOnboardingTemplateHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new UpdateOnboardingTemplateRequest
            {
                CompanyId = companyId,
                Id = template.Id,
                Name = "Template",
                Tasks =
                [
                    new UpdateOnboardingTemplateTaskItem(task.Id, "New Title", "New description", TaskPriority.Critical, OnboardingTemplateTaskAssignTo.NewHire, 3, 0),
                ],
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Tasks);
        var updatedTask = result.Value.Tasks[0];
        Assert.Equal(task.Id, updatedTask.Id);
        Assert.Equal("New Title", updatedTask.Title);
        Assert.Equal("New description", updatedTask.Description);
        Assert.Equal(TaskPriority.Critical, updatedTask.Priority);
        Assert.Equal(OnboardingTemplateTaskAssignTo.NewHire, updatedTask.AssignTo);
        Assert.Equal(3, updatedTask.DueDaysAfterStart);
    }

    [Fact]
    public async Task HandleAsync_Deactivates_Tasks_Not_Present_In_Request()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var template = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Template", null, now);
        var keptTask = template.AddTask(Guid.NewGuid(), "Kept task", null, TaskPriority.Medium, OnboardingTemplateTaskAssignTo.Unassigned, 0, 0, now);
        var removedTask = template.AddTask(Guid.NewGuid(), "Removed task", null, TaskPriority.Medium, OnboardingTemplateTaskAssignTo.Unassigned, 0, 1, now);
        context.OnboardingTemplates.Add(template);
        await context.SaveChangesAsync();

        var handler = new UpdateOnboardingTemplateHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new UpdateOnboardingTemplateRequest
            {
                CompanyId = companyId,
                Id = template.Id,
                Name = "Template",
                Tasks =
                [
                    new UpdateOnboardingTemplateTaskItem(keptTask.Id, "Kept task", null, TaskPriority.Medium, OnboardingTemplateTaskAssignTo.Unassigned, 0, 0),
                ],
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Tasks);
        Assert.Equal(keptTask.Id, result.Value.Tasks[0].Id);

        var saved = await context.OnboardingTemplateTasks.SingleAsync(t => t.Id == removedTask.Id);
        Assert.False(saved.IsActive);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Template_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new UpdateOnboardingTemplateHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new UpdateOnboardingTemplateRequest { CompanyId = Guid.NewGuid(), Id = Guid.NewGuid(), Name = "X", Tasks = [] },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Renaming_To_Existing_Active_Name()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        context.OnboardingTemplates.Add(OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Existing Name", null, now));
        var toRename = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Renaming Template", null, now);
        context.OnboardingTemplates.Add(toRename);
        await context.SaveChangesAsync();

        var handler = new UpdateOnboardingTemplateHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new UpdateOnboardingTemplateRequest
            {
                CompanyId = companyId,
                Id = toRename.Id,
                Name = "Existing Name",
                Tasks = [],
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
