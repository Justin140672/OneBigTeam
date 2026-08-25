using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Offboarding;
using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Features.MarkOffboardingIncompleteOnDepartureFinalised;
using HR.Modules.Offboarding.Persistence;
using HR.Modules.Offboarding.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Offboarding.Tests;

public class MarkOffboardingIncompleteOnDepartureFinalisedHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static OffboardingDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<OffboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static MarkOffboardingIncompleteOnDepartureFinalisedHandler BuildHandler(
        OffboardingDbContext dbContext,
        FakeHrAdministratorDirectory? hrAdministratorDirectory = null,
        FakeNotificationWriter? notificationWriter = null,
        FakeAuditPublisher? auditPublisher = null) =>
        new(
            dbContext,
            new FakeClock(FixedUtcNow),
            hrAdministratorDirectory ?? new FakeHrAdministratorDirectory(),
            notificationWriter ?? new FakeNotificationWriter(),
            auditPublisher ?? new FakeAuditPublisher());

    private static OffboardingPlan SeedPlan(
        OffboardingDbContext dbContext,
        Guid companyId,
        Guid employeeId,
        DateTimeOffset createdAt,
        OffboardingStatus? status = null)
    {
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), companyId, employeeId, DateOnly.FromDateTime(createdAt.Date), null, createdAt);

        if (status == OffboardingStatus.InProgress)
            plan.Start(createdAt);
        else if (status == OffboardingStatus.Completed)
        {
            plan.Start(createdAt);
            plan.Complete(createdAt);
        }
        else if (status == OffboardingStatus.Cancelled)
            plan.Cancel(null, createdAt);

        dbContext.OffboardingPlans.Add(plan);
        return plan;
    }

    private static EmployeeDepartureFinalisedIntegrationEvent BuildEvent(Guid companyId, Guid employeeId) =>
        new(companyId, employeeId, DateOnly.FromDateTime(Now.Date), Now);

    [Fact]
    public async Task HandleAsync_Is_NoOp_When_No_Offboarding_Plan_Exists()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var auditPublisher = new FakeAuditPublisher();
        var notifications = new FakeNotificationWriter();
        var handler = BuildHandler(dbContext, auditPublisher: auditPublisher, notificationWriter: notifications);

        await handler.HandleAsync(BuildEvent(companyId, employeeId), CancellationToken.None);

        Assert.Empty(auditPublisher.Published);
        Assert.Empty(notifications.Written);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HandleAsync_Is_NoOp_When_Most_Recent_Plan_Is_Already_Completed_Or_Cancelled(
        bool isCompleted)
    {
        var status = isCompleted ? OffboardingStatus.Completed : OffboardingStatus.Cancelled;

        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var plan = SeedPlan(dbContext, companyId, employeeId, Now.AddDays(-30), status);
        // Even though the plan has an outstanding task, a terminal plan must never be flagged.
        var pendingTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Return laptop", null,
            OffboardingTaskAssignTo.Employee, null, Now.AddDays(-30));
        dbContext.OffboardingTasks.Add(pendingTask);
        await dbContext.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var handler = BuildHandler(dbContext, auditPublisher: auditPublisher);

        await handler.HandleAsync(BuildEvent(companyId, employeeId), CancellationToken.None);

        Assert.Empty(auditPublisher.Published);
        var savedPlan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.False(savedPlan.HasIncompleteOffboardingAtDeparture);
    }

    [Fact]
    public async Task HandleAsync_Is_NoOp_When_Plan_Has_No_Unresolved_Mandatory_Tasks()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var plan = SeedPlan(dbContext, companyId, employeeId, Now.AddDays(-5), OffboardingStatus.InProgress);
        var completedTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Return laptop", null,
            OffboardingTaskAssignTo.Employee, null, Now.AddDays(-5));
        completedTask.Complete(Now.AddDays(-1));
        dbContext.OffboardingTasks.Add(completedTask);
        await dbContext.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var handler = BuildHandler(dbContext, auditPublisher: auditPublisher);

        await handler.HandleAsync(BuildEvent(companyId, employeeId), CancellationToken.None);

        Assert.Empty(auditPublisher.Published);
        var savedPlan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.False(savedPlan.HasIncompleteOffboardingAtDeparture);
    }

    [Fact]
    public async Task HandleAsync_Flags_Plan_Publishes_Audit_Event_And_Notifies_HR_Administrators_When_Mandatory_Tasks_Unresolved()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var plan = SeedPlan(dbContext, companyId, employeeId, Now.AddDays(-5), OffboardingStatus.InProgress);
        var pendingMandatoryTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Return laptop", null,
            OffboardingTaskAssignTo.Employee, null, Now.AddDays(-5));
        var skippedOptionalTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Optional task", null,
            OffboardingTaskAssignTo.Employee, null, Now.AddDays(-5), isMandatory: false);
        skippedOptionalTask.Skip(Now.AddDays(-1), "Not applicable.", Guid.NewGuid());
        dbContext.OffboardingTasks.AddRange(pendingMandatoryTask, skippedOptionalTask);
        await dbContext.SaveChangesAsync();

        var hrAdmin1 = Guid.NewGuid();
        var hrAdmin2 = Guid.NewGuid();
        var auditPublisher = new FakeAuditPublisher();
        var notifications = new FakeNotificationWriter();
        var handler = BuildHandler(
            dbContext,
            new FakeHrAdministratorDirectory([hrAdmin1, hrAdmin2]),
            notifications,
            auditPublisher);

        await handler.HandleAsync(BuildEvent(companyId, employeeId), CancellationToken.None);

        var savedPlan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.True(savedPlan.HasIncompleteOffboardingAtDeparture);
        Assert.Equal(Now, savedPlan.UpdatedAt);

        var auditEvent = Assert.IsType<OffboardingIncompleteAtDepartureAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(plan.Id, auditEvent.OffboardingPlanId);
        Assert.Equal(employeeId, auditEvent.EmployeeId);
        Assert.Equal(1, auditEvent.OutstandingMandatoryTasks);

        Assert.Equal(2, notifications.Written.Count);
        Assert.All(notifications.Written, n => Assert.Equal(NotificationType.IncompleteOffboardingAtDeparture, n.Type));
        Assert.Contains(notifications.Written, n => n.EmployeeId == hrAdmin1);
        Assert.Contains(notifications.Written, n => n.EmployeeId == hrAdmin2);
    }

    [Fact]
    public async Task HandleAsync_Redelivery_For_Already_Flagged_Plan_Does_Not_Duplicate_Audit_Event_Or_Notifications()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var plan = SeedPlan(dbContext, companyId, employeeId, Now.AddDays(-5), OffboardingStatus.InProgress);
        var pendingMandatoryTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Return laptop", null,
            OffboardingTaskAssignTo.Employee, null, Now.AddDays(-5));
        dbContext.OffboardingTasks.Add(pendingMandatoryTask);
        await dbContext.SaveChangesAsync();

        var hrAdmin = Guid.NewGuid();
        var auditPublisher = new FakeAuditPublisher();
        var notifications = new FakeNotificationWriter();
        var handler = BuildHandler(
            dbContext, new FakeHrAdministratorDirectory([hrAdmin]), notifications, auditPublisher);

        // First delivery flags the plan and raises the exception.
        await handler.HandleAsync(BuildEvent(companyId, employeeId), CancellationToken.None);

        Assert.Single(auditPublisher.Published);
        Assert.Single(notifications.Written);

        // Redelivery of the same integration event (e.g. at-least-once delivery retry) must not
        // duplicate the audit event or notifications, and must leave the flag as-is.
        await handler.HandleAsync(BuildEvent(companyId, employeeId), CancellationToken.None);

        Assert.Single(auditPublisher.Published);
        Assert.Single(notifications.Written);
        var savedPlan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.True(savedPlan.HasIncompleteOffboardingAtDeparture);
    }

    [Fact]
    public async Task HandleAsync_Uses_The_Most_Recently_Created_Plan_When_Employee_Has_Multiple_Plans()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var olderCancelledPlan = SeedPlan(dbContext, companyId, employeeId, Now.AddMonths(-6), OffboardingStatus.Cancelled);
        var newerPlan = SeedPlan(dbContext, companyId, employeeId, Now.AddDays(-2), OffboardingStatus.InProgress);
        var pendingMandatoryTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, newerPlan.Id, "Return laptop", null,
            OffboardingTaskAssignTo.Employee, null, Now.AddDays(-2));
        dbContext.OffboardingTasks.Add(pendingMandatoryTask);
        await dbContext.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var handler = BuildHandler(dbContext, auditPublisher: auditPublisher);

        await handler.HandleAsync(BuildEvent(companyId, employeeId), CancellationToken.None);

        var savedOlderPlan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == olderCancelledPlan.Id);
        Assert.False(savedOlderPlan.HasIncompleteOffboardingAtDeparture);

        var savedNewerPlan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == newerPlan.Id);
        Assert.True(savedNewerPlan.HasIncompleteOffboardingAtDeparture);

        var auditEvent = Assert.IsType<OffboardingIncompleteAtDepartureAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal(newerPlan.Id, auditEvent.OffboardingPlanId);
    }
}
