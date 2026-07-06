using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetOnboardingTemplate;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class GetOnboardingTemplateHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Template_With_Active_Tasks_Ordered_By_DisplayOrder()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var template = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Standard Onboarding", "desc", Now);
        template.AddTask(Guid.NewGuid(), "Second task", null, TaskPriority.Medium, OnboardingTemplateTaskAssignTo.Manager, 1, 1, Now);
        template.AddTask(Guid.NewGuid(), "First task", null, TaskPriority.High, OnboardingTemplateTaskAssignTo.NewHire, 0, 0, Now);
        var removedTaskId = Guid.NewGuid();
        template.AddTask(removedTaskId, "Removed task", null, TaskPriority.Low, OnboardingTemplateTaskAssignTo.Unassigned, 2, 2, Now);
        template.RemoveTask(removedTaskId, Now);

        context.OnboardingTemplates.Add(template);
        await context.SaveChangesAsync();

        var handler = new GetOnboardingTemplateHandler(context);
        var result = await handler.HandleAsync(
            new GetOnboardingTemplateRequest { CompanyId = companyId, Id = template.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Tasks.Count);
        Assert.Equal("First task", result.Value.Tasks[0].Title);
        Assert.Equal("Second task", result.Value.Tasks[1].Title);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Template_Does_Not_Exist()
    {
        await using var context = BuildContext();

        var handler = new GetOnboardingTemplateHandler(context);
        var result = await handler.HandleAsync(
            new GetOnboardingTemplateRequest { CompanyId = Guid.NewGuid(), Id = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Template_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var template = OnboardingTemplate.Create(Guid.NewGuid(), Guid.NewGuid(), "Standard Onboarding", null, Now);
        context.OnboardingTemplates.Add(template);
        await context.SaveChangesAsync();

        var handler = new GetOnboardingTemplateHandler(context);
        var result = await handler.HandleAsync(
            new GetOnboardingTemplateRequest { CompanyId = Guid.NewGuid(), Id = template.Id },
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
