using HR.Infrastructure.Abstractions;
using HR.Modules.Onboarding.Domain;
using HR.Modules.Onboarding.Features.GetOnboardingOverview;
using HR.Modules.Onboarding.Persistence;
using HR.Modules.Onboarding.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Onboarding.Tests;

public class GetOnboardingOverviewHandlerTests
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
        DateTimeOffset createdAt,
        OnboardingStatus? status = null)
    {
        var plan = OnboardingPlan.Create(
            Guid.NewGuid(), companyId, employeeId, DateOnly.FromDateTime(createdAt.Date), null, createdAt);

        if (status == OnboardingStatus.InProgress)
            plan.Start(createdAt);
        else if (status == OnboardingStatus.Completed)
            plan.Complete(createdAt);

        dbContext.OnboardingPlans.Add(plan);
        return plan;
    }

    private static OnboardingTask SeedTask(
        OnboardingDbContext dbContext,
        Guid companyId,
        Guid planId,
        DateTimeOffset createdAt,
        string title = "Some task",
        DateOnly? dueDate = null,
        OnboardingTaskStatus status = OnboardingTaskStatus.Pending)
    {
        var task = OnboardingTask.Create(
            Guid.NewGuid(), companyId, planId, title, null,
            OnboardingTemplateTaskAssignTo.Unassigned, dueDate, createdAt);

        if (status == OnboardingTaskStatus.Completed)
            task.Complete(createdAt);
        else if (status == OnboardingTaskStatus.Skipped)
            task.Skip(createdAt);

        dbContext.OnboardingTasks.Add(task);
        return task;
    }

    private static GetOnboardingOverviewHandler BuildHandler(
        OnboardingDbContext dbContext,
        IOutstandingDocumentRequestReader? documentReader = null,
        IOutstandingAssetAcknowledgementReader? assetReader = null,
        IProbationSummaryReader? probationReader = null) =>
        new(
            dbContext,
            documentReader ?? new FakeOutstandingDocumentRequestReader(),
            assetReader ?? new FakeOutstandingAssetAcknowledgementReader(),
            probationReader ?? new FakeProbationSummaryReader());

    [Fact]
    public async Task HandleAsync_Returns_Plan_And_Tasks_When_Plan_Exists()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var dueDate = new DateOnly(2026, 7, 20);

        var plan = SeedPlan(db, companyId, employeeId, Now, OnboardingStatus.InProgress);
        var task = SeedTask(db, companyId, plan.Id, Now, "Set up workstation", dueDate, OnboardingTaskStatus.Completed);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db);
        var result = await handler.HandleAsync(
            new GetOnboardingOverviewRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.HasPlan);
        Assert.Equal("InProgress", result.PlanStatus);
        Assert.Equal(plan.StartDate, result.StartDate);
        Assert.Single(result.Tasks);
        Assert.Equal(task.Id, result.Tasks[0].Id);
        Assert.Equal("Set up workstation", result.Tasks[0].Title);
        Assert.Equal("Completed", result.Tasks[0].Status);
        Assert.Equal(dueDate, result.Tasks[0].DueDate);
    }

    [Fact]
    public async Task HandleAsync_Returns_HasPlan_False_And_Empty_Tasks_When_No_Plan_Found()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var handler = BuildHandler(db);
        var result = await handler.HandleAsync(
            new GetOnboardingOverviewRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.False(result.HasPlan);
        Assert.Null(result.PlanStatus);
        Assert.Null(result.StartDate);
        Assert.Empty(result.Tasks);
    }

    [Fact]
    public async Task HandleAsync_Populates_CrossModule_Sections_Even_When_No_Plan_Found()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var docItem = new OutstandingDocumentRequestItem(Guid.NewGuid(), "Passport", null, true);
        var assetItem = new OutstandingAssetAcknowledgementItem(Guid.NewGuid(), Guid.NewGuid(), "A001 - Laptop (IT)", Now);
        var probationItem = new ProbationSummaryItem("Active", new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null);

        var handler = BuildHandler(
            db,
            new FakeOutstandingDocumentRequestReader([docItem]),
            new FakeOutstandingAssetAcknowledgementReader([assetItem]),
            new FakeProbationSummaryReader(probationItem));

        var result = await handler.HandleAsync(
            new GetOnboardingOverviewRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.False(result.HasPlan);
        Assert.Empty(result.Tasks);
        Assert.Single(result.OutstandingDocumentRequests);
        Assert.Single(result.OutstandingAssetAcknowledgements);
        Assert.NotNull(result.Probation);
    }

    [Fact]
    public async Task HandleAsync_Returns_Only_Document_Requests_When_Assets_And_Probation_Are_Empty()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var docItem = new OutstandingDocumentRequestItem(Guid.NewGuid(), "Right To Work", new DateOnly(2026, 8, 1), false);

        var handler = BuildHandler(
            db,
            documentReader: new FakeOutstandingDocumentRequestReader([docItem]));

        var result = await handler.HandleAsync(
            new GetOnboardingOverviewRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Single(result.OutstandingDocumentRequests);
        Assert.Equal("Right To Work", result.OutstandingDocumentRequests[0].DocumentTypeName);
        Assert.Empty(result.OutstandingAssetAcknowledgements);
        Assert.Null(result.Probation);
    }

    [Fact]
    public async Task HandleAsync_Returns_Only_Asset_Acknowledgements_When_Documents_And_Probation_Are_Empty()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var assetItem = new OutstandingAssetAcknowledgementItem(Guid.NewGuid(), Guid.NewGuid(), "B099 - Monitor (Peripherals)", Now);

        var handler = BuildHandler(
            db,
            assetReader: new FakeOutstandingAssetAcknowledgementReader([assetItem]));

        var result = await handler.HandleAsync(
            new GetOnboardingOverviewRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Empty(result.OutstandingDocumentRequests);
        Assert.Single(result.OutstandingAssetAcknowledgements);
        Assert.Equal("B099 - Monitor (Peripherals)", result.OutstandingAssetAcknowledgements[0].AssetLabel);
        Assert.Null(result.Probation);
    }

    [Fact]
    public async Task HandleAsync_Returns_Only_Probation_Summary_When_Documents_And_Assets_Are_Empty()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var probationItem = new ProbationSummaryItem("ReviewDue", new DateOnly(2026, 5, 1), new DateOnly(2026, 8, 1), null);

        var handler = BuildHandler(
            db,
            probationReader: new FakeProbationSummaryReader(probationItem));

        var result = await handler.HandleAsync(
            new GetOnboardingOverviewRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Empty(result.OutstandingDocumentRequests);
        Assert.Empty(result.OutstandingAssetAcknowledgements);
        Assert.NotNull(result.Probation);
        Assert.Equal("ReviewDue", result.Probation!.Status);
    }

    [Fact]
    public async Task HandleAsync_Returns_Most_Recent_Plan_When_Employee_Has_Multiple()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var older = SeedPlan(db, companyId, employeeId, Now.AddMonths(-6));
        var newer = SeedPlan(db, companyId, employeeId, Now);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db);
        var result = await handler.HandleAsync(
            new GetOnboardingOverviewRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.HasPlan);
        Assert.Equal(newer.StartDate, result.StartDate);
    }

    [Fact]
    public async Task HandleAsync_Surfaces_CreatedAt_UpdatedAt_And_Null_CompletedAt_For_Pending_Task()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var plan = SeedPlan(db, companyId, employeeId, Now, OnboardingStatus.InProgress);
        var task = SeedTask(db, companyId, plan.Id, Now, "Complete paperwork");
        await db.SaveChangesAsync();

        var handler = BuildHandler(db);
        var result = await handler.HandleAsync(
            new GetOnboardingOverviewRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Single(result.Tasks);
        var item = result.Tasks[0];
        Assert.Equal(task.CreatedAt, item.CreatedAt);
        Assert.Null(item.CompletedAt);
        Assert.Equal(task.UpdatedAt, item.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_Surfaces_CompletedAt_Matching_Completion_Time_For_Completed_Task()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var completedAt = Now.AddDays(3);

        var plan = SeedPlan(db, companyId, employeeId, Now, OnboardingStatus.InProgress);
        var task = OnboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Sign contract", null,
            OnboardingTemplateTaskAssignTo.Unassigned, null, Now);
        task.Complete(completedAt);
        db.OnboardingTasks.Add(task);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db);
        var result = await handler.HandleAsync(
            new GetOnboardingOverviewRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Single(result.Tasks);
        var item = result.Tasks[0];
        Assert.Equal(Now, item.CreatedAt);
        Assert.Equal(completedAt, item.CompletedAt);
        Assert.Equal(completedAt, item.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Return_Plan_For_Different_Company()
    {
        await using var db = BuildContext();
        var employeeId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        SeedPlan(db, otherCompanyId, employeeId, Now);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db);
        var result = await handler.HandleAsync(
            new GetOnboardingOverviewRequest { CompanyId = Guid.NewGuid(), EmployeeId = employeeId },
            CancellationToken.None);

        Assert.False(result.HasPlan);
    }
}
