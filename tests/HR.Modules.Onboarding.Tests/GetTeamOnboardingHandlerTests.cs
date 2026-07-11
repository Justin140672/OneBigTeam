using HR.Infrastructure.Abstractions;
using HR.Modules.Onboarding.Domain;
using HR.Modules.Onboarding.Features.GetTeamOnboarding;
using HR.Modules.Onboarding.Persistence;
using HR.Modules.Onboarding.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Onboarding.Tests;

public class GetTeamOnboardingHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 10, 10, 0, 0, TimeSpan.Zero);

    private static OnboardingDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static OnboardingPlan SeedPlan(
        OnboardingDbContext dbContext,
        Guid companyId,
        Guid employeeId,
        DateOnly startDate,
        OnboardingStatus status)
    {
        var plan = OnboardingPlan.Create(Guid.NewGuid(), companyId, employeeId, startDate, null, Now);

        if (status == OnboardingStatus.InProgress)
            plan.Start(Now);
        else if (status == OnboardingStatus.Completed)
            plan.Complete(Now);
        else if (status == OnboardingStatus.Cancelled)
            plan.Cancel(null, Now);

        dbContext.OnboardingPlans.Add(plan);
        return plan;
    }

    private static void SeedTask(
        OnboardingDbContext dbContext,
        Guid companyId,
        Guid planId,
        OnboardingTaskStatus status)
    {
        var task = OnboardingTask.Create(
            Guid.NewGuid(), companyId, planId, "Some task", null,
            OnboardingTemplateTaskAssignTo.Unassigned, null, Now);

        if (status == OnboardingTaskStatus.Completed)
            task.Complete(Now);
        else if (status == OnboardingTaskStatus.Skipped)
            task.Skip(Now);

        dbContext.OnboardingTasks.Add(task);
    }

    private static GetTeamOnboardingHandler BuildHandler(
        OnboardingDbContext dbContext,
        Guid[]? directReportIds = null,
        Dictionary<Guid, string>? names = null) =>
        new(
            dbContext,
            new FakeDirectReportsReader(directReportIds ?? []),
            new FakeEmployeeNameReader(names));

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_Manager_Has_No_Direct_Reports()
    {
        await using var db = BuildContext();
        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            new GetTeamOnboardingRequest { CompanyId = Guid.NewGuid(), ManagerId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_Returns_Active_Plan_With_Correct_Task_Counts_And_Percent()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var plan = SeedPlan(db, companyId, employeeId, new DateOnly(2026, 7, 1), OnboardingStatus.InProgress);
        SeedTask(db, companyId, plan.Id, OnboardingTaskStatus.Completed);
        SeedTask(db, companyId, plan.Id, OnboardingTaskStatus.Skipped);
        SeedTask(db, companyId, plan.Id, OnboardingTaskStatus.Pending);
        SeedTask(db, companyId, plan.Id, OnboardingTaskStatus.Pending);
        await db.SaveChangesAsync();

        var names = new Dictionary<Guid, string> { [employeeId] = "Jamie Fox" };
        var handler = BuildHandler(db, [employeeId], names);

        var result = await handler.HandleAsync(
            new GetTeamOnboardingRequest { CompanyId = companyId, ManagerId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Single(result.Items);
        var item = result.Items[0];
        Assert.Equal(employeeId, item.EmployeeId);
        Assert.Equal("Jamie Fox", item.EmployeeName);
        Assert.Equal("InProgress", item.PlanStatus);
        Assert.Equal(plan.StartDate, item.StartDate);
        Assert.Equal(4, item.TotalTasks);
        Assert.Equal(2, item.CompletedTasks);
        Assert.Equal(50, item.PercentComplete);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Plans_That_Are_Completed()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        SeedPlan(db, companyId, employeeId, new DateOnly(2026, 7, 1), OnboardingStatus.Completed);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db, [employeeId]);

        var result = await handler.HandleAsync(
            new GetTeamOnboardingRequest { CompanyId = companyId, ManagerId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Plans_That_Are_Cancelled()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        SeedPlan(db, companyId, employeeId, new DateOnly(2026, 7, 1), OnboardingStatus.Cancelled);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db, [employeeId]);

        var result = await handler.HandleAsync(
            new GetTeamOnboardingRequest { CompanyId = companyId, ManagerId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_Returns_Zero_Percent_Complete_When_Plan_Has_No_Tasks()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        SeedPlan(db, companyId, employeeId, new DateOnly(2026, 7, 1), OnboardingStatus.NotStarted);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db, [employeeId]);

        var result = await handler.HandleAsync(
            new GetTeamOnboardingRequest { CompanyId = companyId, ManagerId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal(0, result.Items[0].TotalTasks);
        Assert.Equal(0, result.Items[0].CompletedTasks);
        Assert.Equal(0, result.Items[0].PercentComplete);
    }

    [Fact]
    public async Task HandleAsync_Orders_Results_By_StartDate_Ascending()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();

        var laterPlan = SeedPlan(db, companyId, employeeA, new DateOnly(2026, 8, 1), OnboardingStatus.InProgress);
        var earlierPlan = SeedPlan(db, companyId, employeeB, new DateOnly(2026, 7, 1), OnboardingStatus.NotStarted);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db, [employeeA, employeeB]);

        var result = await handler.HandleAsync(
            new GetTeamOnboardingRequest { CompanyId = companyId, ManagerId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(earlierPlan.EmployeeId, result.Items[0].EmployeeId);
        Assert.Equal(laterPlan.EmployeeId, result.Items[1].EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Falls_Back_To_Employee_When_Name_Not_Found()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        SeedPlan(db, companyId, employeeId, new DateOnly(2026, 7, 1), OnboardingStatus.InProgress);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db, [employeeId]);

        var result = await handler.HandleAsync(
            new GetTeamOnboardingRequest { CompanyId = companyId, ManagerId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("Employee", result.Items[0].EmployeeName);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Return_Plan_For_Different_Company()
    {
        await using var db = BuildContext();
        var employeeId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        SeedPlan(db, otherCompanyId, employeeId, new DateOnly(2026, 7, 1), OnboardingStatus.InProgress);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db, [employeeId]);

        var result = await handler.HandleAsync(
            new GetTeamOnboardingRequest { CompanyId = Guid.NewGuid(), ManagerId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Empty(result.Items);
    }
}
