using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Features.GetOffboardingOverview;
using HR.Modules.Offboarding.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Offboarding.Tests;

public class GetOffboardingOverviewHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 10, 10, 0, 0, TimeSpan.Zero);

    private static OffboardingDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<OffboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static OffboardingPlan SeedPlan(
        OffboardingDbContext dbContext,
        Guid companyId,
        Guid employeeId,
        DateTimeOffset createdAt,
        DateOnly? lastWorkingDay = null,
        string? notes = null,
        OffboardingStatus? status = null)
    {
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), companyId, employeeId,
            lastWorkingDay ?? DateOnly.FromDateTime(createdAt.Date),
            notes, createdAt);

        if (status == OffboardingStatus.InProgress)
            plan.Start(createdAt);
        else if (status == OffboardingStatus.Completed)
        {
            plan.Start(createdAt);
            plan.Complete(createdAt);
        }
        else if (status == OffboardingStatus.Cancelled)
            plan.Cancel(notes, createdAt);

        dbContext.OffboardingPlans.Add(plan);
        return plan;
    }

    private static OffboardingTask SeedTask(
        OffboardingDbContext dbContext,
        Guid companyId,
        Guid planId,
        DateTimeOffset createdAt,
        string title = "Some task",
        string? description = null,
        OffboardingTaskAssignTo assignTo = OffboardingTaskAssignTo.Employee,
        DateOnly? dueDate = null,
        OffboardingTaskStatus status = OffboardingTaskStatus.Pending,
        DateTimeOffset? completedAt = null)
    {
        var task = OffboardingTask.Create(
            Guid.NewGuid(), companyId, planId, title, description, assignTo, dueDate, createdAt);

        if (status == OffboardingTaskStatus.Completed)
            task.Complete(completedAt ?? createdAt);
        else if (status == OffboardingTaskStatus.Skipped)
            task.Skip(createdAt, "Skipped for test.", Guid.NewGuid());

        dbContext.OffboardingTasks.Add(task);
        return task;
    }

    private static GetOffboardingOverviewHandler BuildHandler(OffboardingDbContext dbContext) => new(dbContext);

    [Fact]
    public async Task HandleAsync_Returns_HasPlan_False_And_Empty_Tasks_When_No_Plan_Found()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var handler = BuildHandler(db);
        var result = await handler.HandleAsync(
            new GetOffboardingOverviewRequest(companyId, employeeId),
            CancellationToken.None);

        Assert.Equal(employeeId, result.EmployeeId);
        Assert.False(result.HasPlan);
        Assert.Null(result.PlanStatus);
        Assert.Null(result.LastWorkingDay);
        Assert.Null(result.Notes);
        Assert.Empty(result.Tasks);
    }

    [Fact]
    public async Task HandleAsync_Returns_Plan_With_Empty_Tasks_When_Plan_Has_No_Tasks()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var lastWorkingDay = new DateOnly(2026, 8, 1);

        var plan = SeedPlan(db, companyId, employeeId, Now, lastWorkingDay, "Resigned.", OffboardingStatus.InProgress);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db);
        var result = await handler.HandleAsync(
            new GetOffboardingOverviewRequest(companyId, employeeId),
            CancellationToken.None);

        Assert.True(result.HasPlan);
        Assert.Equal("InProgress", result.PlanStatus);
        Assert.Equal(lastWorkingDay, result.LastWorkingDay);
        Assert.Equal("Resigned.", result.Notes);
        Assert.Empty(result.Tasks);

        var persistedPlan = await db.OffboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(OffboardingStatus.InProgress, persistedPlan.Status);
    }

    [Fact]
    public async Task HandleAsync_Maps_All_Tasks_With_Enum_To_String_Conversion_For_Mixed_AssignTo_And_Status()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var dueDate = new DateOnly(2026, 8, 1);

        var plan = SeedPlan(db, companyId, employeeId, Now, dueDate, null, OffboardingStatus.InProgress);

        var employeeTask = SeedTask(
            db, companyId, plan.Id, Now, "Return asset: Laptop", null,
            OffboardingTaskAssignTo.Employee, dueDate, OffboardingTaskStatus.Pending);
        var managerTask = SeedTask(
            db, companyId, plan.Id, Now, "Conduct exit interview", null,
            OffboardingTaskAssignTo.Manager, dueDate, OffboardingTaskStatus.Pending);
        var hrTask = SeedTask(
            db, companyId, plan.Id, Now, "Review outstanding documents", "1 outstanding document(s).",
            OffboardingTaskAssignTo.HR, dueDate, OffboardingTaskStatus.Completed, Now.AddDays(1));
        var skippedTask = SeedTask(
            db, companyId, plan.Id, Now, "Arrange handover", null,
            OffboardingTaskAssignTo.Manager, dueDate, OffboardingTaskStatus.Skipped);

        await db.SaveChangesAsync();

        var handler = BuildHandler(db);
        var result = await handler.HandleAsync(
            new GetOffboardingOverviewRequest(companyId, employeeId),
            CancellationToken.None);

        Assert.True(result.HasPlan);
        Assert.Equal(4, result.Tasks.Count);

        var employeeItem = Assert.Single(result.Tasks, t => t.Id == employeeTask.Id);
        Assert.Equal("Return asset: Laptop", employeeItem.Title);
        Assert.Equal("Employee", employeeItem.AssignTo);
        Assert.Equal("Pending", employeeItem.Status);
        Assert.Equal(dueDate, employeeItem.DueDate);
        Assert.Null(employeeItem.CompletedAt);

        var managerItem = Assert.Single(result.Tasks, t => t.Id == managerTask.Id);
        Assert.Equal("Manager", managerItem.AssignTo);
        Assert.Equal("Pending", managerItem.Status);
        Assert.Null(managerItem.CompletedAt);

        var hrItem = Assert.Single(result.Tasks, t => t.Id == hrTask.Id);
        Assert.Equal("HR", hrItem.AssignTo);
        Assert.Equal("Completed", hrItem.Status);
        Assert.Equal("1 outstanding document(s).", hrItem.Description);
        Assert.Equal(Now.AddDays(1), hrItem.CompletedAt);
        Assert.Equal(Now.AddDays(1), hrItem.UpdatedAt);

        var skippedItem = Assert.Single(result.Tasks, t => t.Id == skippedTask.Id);
        Assert.Equal("Manager", skippedItem.AssignTo);
        Assert.Equal("Skipped", skippedItem.Status);
        Assert.Null(skippedItem.CompletedAt);
    }

    // ---- OFF-07 ----

    [Fact]
    public async Task HandleAsync_Returns_HasIncompleteOffboardingAtDeparture_Progress_Fields_When_No_Plan_Found()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var handler = BuildHandler(db);
        var result = await handler.HandleAsync(
            new GetOffboardingOverviewRequest(companyId, employeeId),
            CancellationToken.None);

        Assert.False(result.HasIncompleteOffboardingAtDeparture);
        Assert.Equal(0, result.TotalTasks);
        Assert.Equal(0, result.ResolvedTasks);
        Assert.Equal(0, result.ProgressPercent);
    }

    [Fact]
    public async Task HandleAsync_Surfaces_HasIncompleteOffboardingAtDeparture_And_Progress_Fields_And_Task_MandatorySkipFields()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), companyId, employeeId, DateOnly.FromDateTime(Now.Date), null, Now);
        plan.Start(Now);
        plan.MarkIncompleteOffboardingAtDeparture(Now);
        db.OffboardingPlans.Add(plan);

        var completedTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Return laptop", null,
            OffboardingTaskAssignTo.Employee, null, Now);
        completedTask.Complete(Now);

        var skippedTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Optional handover note", null,
            OffboardingTaskAssignTo.Manager, null, Now, isMandatory: false);
        var skipActor = Guid.NewGuid();
        skippedTask.Skip(Now, "Not applicable for this role.", skipActor);

        var pendingMandatoryTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Confirm exit interview", null,
            OffboardingTaskAssignTo.Manager, null, Now);

        db.OffboardingTasks.AddRange(completedTask, skippedTask, pendingMandatoryTask);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db);
        var result = await handler.HandleAsync(
            new GetOffboardingOverviewRequest(companyId, employeeId),
            CancellationToken.None);

        Assert.True(result.HasIncompleteOffboardingAtDeparture);
        Assert.Equal(3, result.TotalTasks);
        Assert.Equal(2, result.ResolvedTasks); // Completed + Skipped
        Assert.Equal(67, result.ProgressPercent); // 2/3 rounded

        var skippedItem = Assert.Single(result.Tasks, t => t.Id == skippedTask.Id);
        Assert.False(skippedItem.IsMandatory);
        Assert.Equal("Not applicable for this role.", skippedItem.SkipReason);
        Assert.Equal(skipActor, skippedItem.SkippedByUserId);
        Assert.Equal(Now, skippedItem.SkippedAt);

        var completedItem = Assert.Single(result.Tasks, t => t.Id == completedTask.Id);
        Assert.True(completedItem.IsMandatory);
        Assert.Null(completedItem.SkipReason);
    }

    [Fact]
    public async Task HandleAsync_Surfaces_CreatedAt_And_UpdatedAt_For_Each_Task()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var plan = SeedPlan(db, companyId, employeeId, Now, status: OffboardingStatus.InProgress);
        var task = SeedTask(db, companyId, plan.Id, Now, "Some task");
        await db.SaveChangesAsync();

        var handler = BuildHandler(db);
        var result = await handler.HandleAsync(
            new GetOffboardingOverviewRequest(companyId, employeeId),
            CancellationToken.None);

        var item = Assert.Single(result.Tasks);
        Assert.Equal(task.CreatedAt, item.CreatedAt);
        Assert.Equal(task.UpdatedAt, item.UpdatedAt);
        Assert.Null(item.CompletedAt);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Return_Plan_Or_Tasks_For_Different_Company()
    {
        await using var db = BuildContext();
        var employeeId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        var plan = SeedPlan(db, otherCompanyId, employeeId, Now, status: OffboardingStatus.InProgress);
        SeedTask(db, otherCompanyId, plan.Id, Now);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db);
        var result = await handler.HandleAsync(
            new GetOffboardingOverviewRequest(Guid.NewGuid(), employeeId),
            CancellationToken.None);

        Assert.False(result.HasPlan);
        Assert.Empty(result.Tasks);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Return_Plan_For_Different_Employee()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();

        SeedPlan(db, companyId, otherEmployeeId, Now, status: OffboardingStatus.InProgress);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db);
        var result = await handler.HandleAsync(
            new GetOffboardingOverviewRequest(companyId, employeeId),
            CancellationToken.None);

        Assert.False(result.HasPlan);
        Assert.Empty(result.Tasks);
    }

    [Fact]
    public async Task HandleAsync_Returns_Most_Recent_Plan_When_Employee_Has_Multiple_Plans()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var older = SeedPlan(
            db, companyId, employeeId, Now.AddMonths(-6),
            new DateOnly(2026, 1, 1), "Old plan", OffboardingStatus.Completed);
        var newer = SeedPlan(
            db, companyId, employeeId, Now,
            new DateOnly(2026, 8, 1), "New plan", OffboardingStatus.InProgress);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db);
        var result = await handler.HandleAsync(
            new GetOffboardingOverviewRequest(companyId, employeeId),
            CancellationToken.None);

        Assert.True(result.HasPlan);
        Assert.Equal(newer.LastWorkingDay, result.LastWorkingDay);
        Assert.Equal("New plan", result.Notes);
        Assert.Equal("InProgress", result.PlanStatus);
        Assert.NotEqual(older.LastWorkingDay, result.LastWorkingDay);
    }

    // ---- OFF-05 ----

    [Fact]
    public async Task HandleAsync_Returns_IsBackdated_And_RequiresHrReconciliation_False_When_No_Plan_Found()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var handler = BuildHandler(db);
        var result = await handler.HandleAsync(
            new GetOffboardingOverviewRequest(companyId, employeeId),
            CancellationToken.None);

        Assert.False(result.IsBackdated);
        Assert.False(result.RequiresHrReconciliation);
    }

    [Fact]
    public async Task HandleAsync_Surfaces_IsBackdated_RequiresHrReconciliation_And_Task_RequiresHrConfirmation()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), companyId, employeeId, DateOnly.FromDateTime(Now.Date), null, Now, isBackdated: true);
        plan.Start(Now);
        plan.MarkHrReconciliationRequired(Now);
        db.OffboardingPlans.Add(plan);

        var reconciliationTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Confirm return of asset", null,
            OffboardingTaskAssignTo.HR, null, Now, requiresHrConfirmation: true);
        db.OffboardingTasks.Add(reconciliationTask);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db);
        var result = await handler.HandleAsync(
            new GetOffboardingOverviewRequest(companyId, employeeId),
            CancellationToken.None);

        Assert.True(result.IsBackdated);
        Assert.True(result.RequiresHrReconciliation);
        var taskItem = Assert.Single(result.Tasks);
        Assert.True(taskItem.RequiresHrConfirmation);
    }
}
