using HR.Infrastructure.Abstractions;
using HR.Modules.Onboarding.Domain;
using HR.Modules.Onboarding.Features.GetMyOnboardingStatus;
using HR.Modules.Onboarding.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Onboarding.Tests;

public class GetMyOnboardingStatusHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 10, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_HasPlan_False_When_No_Plan_Exists()
    {
        await using var dbContext = BuildContext();
        var handler = new GetMyOnboardingStatusHandler(dbContext);

        var result = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.HasPlan);
        Assert.Null(result.PlanStatus);
        Assert.Null(result.StartDate);
        Assert.Equal(0, result.TotalTasks);
        Assert.Equal(0, result.CompletedTasks);
        Assert.Empty(result.Tasks);
    }

    [Fact]
    public async Task HandleAsync_Returns_Plan_With_Zero_Tasks()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var plan = OnboardingPlan.Create(Guid.NewGuid(), companyId, employeeId, new DateOnly(2026, 7, 1), null, Now);
        dbContext.OnboardingPlans.Add(plan);
        await dbContext.SaveChangesAsync();

        var handler = new GetMyOnboardingStatusHandler(dbContext);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.HasPlan);
        Assert.Equal("NotStarted", result.PlanStatus);
        Assert.Equal(new DateOnly(2026, 7, 1), result.StartDate);
        Assert.Equal(0, result.TotalTasks);
        Assert.Equal(0, result.CompletedTasks);
        Assert.Empty(result.Tasks);
    }

    [Fact]
    public async Task HandleAsync_Counts_Only_Completed_Tasks_Not_Skipped()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var plan = OnboardingPlan.Create(Guid.NewGuid(), companyId, employeeId, new DateOnly(2026, 7, 1), null, Now);
        plan.Start(Now);
        dbContext.OnboardingPlans.Add(plan);

        var pending = OnboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Complete paperwork", null,
            OnboardingTemplateTaskAssignTo.NewHire, new DateOnly(2026, 7, 3), Now);

        var completed = OnboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Set up workstation", null,
            OnboardingTemplateTaskAssignTo.Manager, new DateOnly(2026, 7, 2), Now);
        completed.Complete(Now);

        var skipped = OnboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Optional training", null,
            OnboardingTemplateTaskAssignTo.NewHire, new DateOnly(2026, 7, 5), Now);
        skipped.Skip(Now);

        dbContext.OnboardingTasks.AddRange(pending, completed, skipped);
        await dbContext.SaveChangesAsync();

        var handler = new GetMyOnboardingStatusHandler(dbContext);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.HasPlan);
        Assert.Equal(3, result.TotalTasks);
        Assert.Equal(1, result.CompletedTasks);

        // Ordered by DueDate ascending: completed (7/2), pending (7/3), skipped (7/5).
        Assert.Equal(
            [completed.Id, pending.Id, skipped.Id],
            result.Tasks.Select(t => t.Id).ToArray());

        var completedItem = result.Tasks.Single(t => t.Id == completed.Id);
        Assert.Equal("Completed", completedItem.Status);
        Assert.Equal(Now, completedItem.CompletedAt);

        var skippedItem = result.Tasks.Single(t => t.Id == skipped.Id);
        Assert.Equal("Skipped", skippedItem.Status);
        Assert.Null(skippedItem.CompletedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_Most_Recently_Created_Plan_When_Employee_Has_Multiple()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var older = OnboardingPlan.Create(Guid.NewGuid(), companyId, employeeId, new DateOnly(2025, 1, 1), null, Now.AddMonths(-6));
        var newer = OnboardingPlan.Create(Guid.NewGuid(), companyId, employeeId, new DateOnly(2026, 7, 1), null, Now);
        dbContext.OnboardingPlans.AddRange(older, newer);
        await dbContext.SaveChangesAsync();

        var handler = new GetMyOnboardingStatusHandler(dbContext);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.HasPlan);
        Assert.Equal(new DateOnly(2026, 7, 1), result.StartDate);
    }

    [Fact]
    public async Task HandleAsync_Isolates_By_Company_And_Employee()
    {
        await using var dbContext = BuildContext();
        var employeeId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();

        var otherCompanyPlan = OnboardingPlan.Create(Guid.NewGuid(), otherCompanyId, employeeId, new DateOnly(2026, 7, 1), null, Now);
        var otherEmployeePlan = OnboardingPlan.Create(Guid.NewGuid(), companyId, otherEmployeeId, new DateOnly(2026, 7, 1), null, Now);
        dbContext.OnboardingPlans.AddRange(otherCompanyPlan, otherEmployeePlan);
        await dbContext.SaveChangesAsync();

        var handler = new GetMyOnboardingStatusHandler(dbContext);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.False(result.HasPlan);
    }

    private static OnboardingDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
