using System.Net;
using System.Net.Http.Json;
using HR.Infrastructure.Abstractions;
using HR.Infrastructure.Persistence;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Notifications.Persistence;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Jobs;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Jobs;
using HR.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Cross-module PostgreSQL coverage of durable operator-recovery audit intents, automatic Recruitment
/// unblocking after a Tasks reset, and the terminal state of interactive completion effects.
/// </summary>
[Collection("Integration")]
public class TaskCompletionRecoveryDurabilityIntegrationTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid OperatorUser = new("cc0000d3-0000-0000-0000-000000000001");
    private static readonly Guid EmployeeUser = new("cc0000d3-0000-0000-0000-000000000002");

    public TaskCompletionRecoveryDurabilityIntegrationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, OperatorUser, SystemRoles.CompanyAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, EmployeeUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientAsync(Guid userId, Guid roleId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, roleId, companyId);
        return client;
    }

    private static string TasksResetUrl(Guid companyId, Guid operationId) =>
        $"/api/companies/{companyId}/tasks/programmatic-completions/{operationId}/reset";

    private static string InteractiveResetUrl(Guid companyId, Guid operationId) =>
        $"/api/companies/{companyId}/tasks/completion-operations/{operationId}/reset";

    private static string RetryUrl(Guid companyId, Guid reconciliationId) =>
        $"/api/companies/{companyId}/recruitment/interview-outcome-reconciliations/{reconciliationId}/retry";

    private async Task RunTasksAuditDeliveryAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TaskRecoveryAuditDeliveryJob>().ExecuteAsync();
    }

    private async Task RunRecruitmentSweepAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<InterviewTaskCleanupReconciliationJob>().ExecuteAsync();
    }

    private async Task<T> TasksAsync<T>(Func<TasksDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<TasksDbContext>());
    }

    private async Task<T> RecruitmentAsync<T>(Func<RecruitmentDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>());
    }

    private async Task<int> AuditCountAsync(Guid eventId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AuditDbContext>()
            .AuditEvents.CountAsync(e => e.EventId == eventId);
    }

    private Task<InterviewOutcomeTaskReconciliation> ReconciliationAsync(Guid reconciliationId) =>
        RecruitmentAsync(db => db.InterviewOutcomeTaskReconciliations.AsNoTracking().SingleAsync(r => r.Id == reconciliationId));

    private Task<List<TaskRecoveryAction>> TaskActionsAsync(Guid operationId) =>
        TasksAsync(db => db.TaskRecoveryActions.AsNoTracking().Where(a => a.OperationId == operationId).OrderBy(a => a.SequenceNumber).ToListAsync());

    private Task<List<InterviewOutcomeRepairAction>> RepairActionsAsync(Guid reconciliationId) =>
        RecruitmentAsync(db => db.InterviewOutcomeRepairActions.AsNoTracking().Where(a => a.ReconciliationId == reconciliationId).OrderBy(a => a.SequenceNumber).ToListAsync());

    [Fact]
    public async Task Tasks_Reset_Succeeds_While_Audit_Is_Unavailable_Then_Background_Delivery_Creates_The_Audit_Without_Repeating_The_Reset()
    {
        var blocked = await InterviewRecoverySeeder.SeedBlockedAsync(_factory);
        var eventId = TaskRecoveryAction.EventIdFor(blocked.TasksOperationId, 1);
        using var client = await ClientAsync(OperatorUser, SystemRoles.CompanyAdministrator, blocked.CompanyId);
        _factory.AuditFaultInjector.Drop(eventId);

        try
        {
            var response = await client.PostAsJsonAsync(
                TasksResetUrl(blocked.CompanyId, blocked.TasksOperationId), new { reason = "Audit store outage" });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var payload = await response.Content.ReadFromJsonAsync<ResetPayload>();
            Assert.True(payload!.WasReset);
            Assert.Equal(eventId, payload.RecoveryActionId);

            var pending = Assert.Single(await TaskActionsAsync(blocked.TasksOperationId));
            Assert.Equal(eventId, pending.Id);
            Assert.Null(pending.AuditDeliveredAt);
            Assert.Equal(0, await AuditCountAsync(eventId));
        }
        finally
        {
            _factory.AuditFaultInjector.Restore(eventId);
        }

        await RunTasksAuditDeliveryAsync();
        await RunTasksAuditDeliveryAsync();

        Assert.NotNull(Assert.Single(await TaskActionsAsync(blocked.TasksOperationId)).AuditDeliveredAt);
        Assert.Equal(1, await AuditCountAsync(eventId));
        Assert.Equal(2, _factory.AuditFaultInjector.PublishAttempts(eventId));
        var state = await TasksAsync(db => db.ProgrammaticTaskCompletions.AsNoTracking().SingleAsync(c => c.TaskId == blocked.TaskId));
        Assert.Equal(1, state.ResetCount);
    }

    [Fact]
    public async Task Recruitment_Repair_Succeeds_While_Audit_Is_Unavailable_Then_The_Sweep_Delivers_The_Audit_Without_Repeating_The_Unblock()
    {
        var blocked = await InterviewRecoverySeeder.SeedBlockedAsync(_factory);
        var eventId = InterviewOutcomeRepairAction.EventIdFor(blocked.ReconciliationId, 1);
        using var client = await ClientAsync(OperatorUser, SystemRoles.CompanyAdministrator, blocked.CompanyId);
        _factory.AuditFaultInjector.Drop(eventId);

        try
        {
            var response = await client.PostAsJsonAsync(
                RetryUrl(blocked.CompanyId, blocked.ReconciliationId), new { reason = "Audit store outage" });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var pending = Assert.Single(await RepairActionsAsync(blocked.ReconciliationId));
            Assert.Equal(eventId, pending.Id);
            Assert.Null(pending.AuditDeliveredAt);
            Assert.False((await ReconciliationAsync(blocked.ReconciliationId)).IsBlocked);
            Assert.Equal(0, await AuditCountAsync(eventId));
        }
        finally
        {
            _factory.AuditFaultInjector.Restore(eventId);
        }

        await RunRecruitmentSweepAsync();
        await RunRecruitmentSweepAsync();

        Assert.NotNull(Assert.Single(await RepairActionsAsync(blocked.ReconciliationId)).AuditDeliveredAt);
        Assert.Equal(1, await AuditCountAsync(eventId));
        Assert.Equal(1, (await ReconciliationAsync(blocked.ReconciliationId)).RepairCount);
    }

    [Fact]
    public async Task Direct_Tasks_Reset_Automatically_Unblocks_The_Linked_Recruitment_Reconciliation()
    {
        var blocked = await InterviewRecoverySeeder.SeedBlockedAsync(_factory);
        using var client = await ClientAsync(OperatorUser, SystemRoles.CompanyAdministrator, blocked.CompanyId);

        var response = await client.PostAsJsonAsync(
            TasksResetUrl(blocked.CompanyId, blocked.TasksOperationId), new { reason = "Fixed upstream" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await ReconciliationAsync(blocked.ReconciliationId)).IsBlocked);

        await RunRecruitmentSweepAsync();

        var record = await ReconciliationAsync(blocked.ReconciliationId);
        Assert.False(record.IsBlocked);
        Assert.Equal(1, record.RepairCount);
        Assert.Equal(OperatorUser, record.LastRepairedBy);
        var action = Assert.Single(await RepairActionsAsync(blocked.ReconciliationId));
        Assert.Equal(InterviewOutcomeRepairAction.SourceTasksReset, action.Source);
        Assert.Equal(blocked.TasksOperationId, action.TasksOperationId);
        Assert.NotNull(action.AuditDeliveredAt);
        Assert.Equal(1, await AuditCountAsync(action.Id));
    }

    [Fact]
    public async Task A_Crash_Between_The_Tasks_Reset_And_The_Recruitment_Unblock_Is_Recovered_By_A_Later_Sweep()
    {
        var blocked = await InterviewRecoverySeeder.SeedBlockedAsync(_factory);
        using (var scope = _factory.Services.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<ITaskCompletionRecovery>().ResetTerminalCompletionAsync(
                blocked.CompanyId, blocked.TasksOperationId, OperatorUser, "Reset then crash", CancellationToken.None);
            Assert.Equal(TaskCompletionResetOutcome.Reset, result.Outcome);
        }

        Assert.True((await ReconciliationAsync(blocked.ReconciliationId)).IsBlocked);

        await RunRecruitmentSweepAsync();
        await RunRecruitmentSweepAsync();

        var record = await ReconciliationAsync(blocked.ReconciliationId);
        Assert.False(record.IsBlocked);
        Assert.Equal(1, record.RepairCount);
        Assert.Single(await RepairActionsAsync(blocked.ReconciliationId));
        Assert.Equal(1, (await TaskActionsAsync(blocked.TasksOperationId)).Count);
    }

    [Fact]
    public async Task Concurrent_Tasks_Reset_And_Recruitment_Repair_Converge_Without_Duplicate_Resets_Repairs_Or_Audits()
    {
        var blocked = await InterviewRecoverySeeder.SeedBlockedAsync(_factory);
        using var tasksClient = await ClientAsync(OperatorUser, SystemRoles.CompanyAdministrator, blocked.CompanyId);
        using var recruitmentClient = await ClientAsync(OperatorUser, SystemRoles.CompanyAdministrator, blocked.CompanyId);

        var responses = await Task.WhenAll(
            Task.Run(() => tasksClient.PostAsJsonAsync(
                TasksResetUrl(blocked.CompanyId, blocked.TasksOperationId), new { reason = "Concurrent" })),
            Task.Run(() => recruitmentClient.PostAsJsonAsync(
                RetryUrl(blocked.CompanyId, blocked.ReconciliationId), new { reason = "Concurrent" })));

        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));

        await RunRecruitmentSweepAsync();
        await RunTasksAuditDeliveryAsync();
        await RunRecruitmentSweepAsync();

        var state = await TasksAsync(db => db.ProgrammaticTaskCompletions.AsNoTracking().SingleAsync(c => c.TaskId == blocked.TaskId));
        Assert.Equal(1, state.ResetCount);
        var taskAction = Assert.Single(await TaskActionsAsync(blocked.TasksOperationId));
        Assert.NotNull(taskAction.AuditDeliveredAt);
        Assert.Equal(1, await AuditCountAsync(taskAction.Id));

        var record = await ReconciliationAsync(blocked.ReconciliationId);
        Assert.False(record.IsBlocked);
        Assert.Equal(1, record.RepairCount);
        var repairAction = Assert.Single(await RepairActionsAsync(blocked.ReconciliationId));
        Assert.NotNull(repairAction.AuditDeliveredAt);
        Assert.Equal(1, await AuditCountAsync(repairAction.Id));
    }

    [Fact]
    public async Task An_Unrelated_Company_Cannot_Reset_Or_Repair_Another_Companys_Blocked_Work()
    {
        var blocked = await InterviewRecoverySeeder.SeedBlockedAsync(_factory);
        var otherCompany = Guid.NewGuid();
        using var client = await ClientAsync(OperatorUser, SystemRoles.CompanyAdministrator, otherCompany);

        var reset = await client.PostAsJsonAsync(TasksResetUrl(otherCompany, blocked.TasksOperationId), new { reason = "x" });
        var interactiveReset = await client.PostAsJsonAsync(InteractiveResetUrl(otherCompany, blocked.TasksOperationId), new { reason = "x" });
        var repair = await client.PostAsJsonAsync(RetryUrl(otherCompany, blocked.ReconciliationId), new { reason = "x" });

        Assert.Equal(HttpStatusCode.NotFound, reset.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, interactiveReset.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, repair.StatusCode);
        Assert.True((await ReconciliationAsync(blocked.ReconciliationId)).IsBlocked);
        Assert.Empty(await TaskActionsAsync(blocked.TasksOperationId));
        Assert.Empty(await RepairActionsAsync(blocked.ReconciliationId));
    }

    [Fact]
    public async Task Operator_Retry_Of_Terminal_Interactive_Effects_Resumes_Only_Notification_And_Audit_Delivery()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var (operationId, taskId) = await SeedTerminalInteractiveOperationAsync(companyId, employeeId);
        using var client = await ClientAsync(OperatorUser, SystemRoles.CompanyAdministrator, companyId);

        var response = await client.PostAsJsonAsync(
            InteractiveResetUrl(companyId, operationId), new { reason = "Notification store recovered" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ResetPayload>();
        Assert.True(payload!.WasReset);
        Assert.Equal("interactive", payload.OperationKind);
        Assert.Equal(1, payload.ResetCount);

        var afterReset = await TasksAsync(db => db.TaskCompletionOperations.AsNoTracking().SingleAsync(o => o.Id == operationId));
        Assert.Equal(TaskCompletionOperation.StatusDispatchApplied, afterReset.Status);
        Assert.Null(afterReset.TerminalFailureAt);
        Assert.Equal(OperatorUser, afterReset.LastResetBy);

        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<TaskCompletionEffectsJob>().ProcessAsync(operationId, companyId);

        var processed = await TasksAsync(db => db.TaskCompletionOperations.AsNoTracking().SingleAsync(o => o.Id == operationId));
        Assert.Equal(TaskCompletionOperation.StatusProcessed, processed.Status);
        Assert.Equal(1, await AuditCountAsync(taskId));
        using var scope2 = _factory.Services.CreateScope();
        Assert.Equal(1, await scope2.ServiceProvider.GetRequiredService<NotificationsDbContext>()
            .Notifications.CountAsync(n => n.SourceEntityId == taskId && n.Type == NotificationType.TaskCompleted));

        var action = Assert.Single(await TaskActionsAsync(operationId));
        Assert.Equal(TaskRecoveryAction.KindInteractive, action.OperationKind);
        Assert.NotNull(action.AuditDeliveredAt);
        Assert.Equal(1, await AuditCountAsync(action.Id));
    }

    [Fact]
    public async Task Interactive_Reset_Endpoint_Rejects_Anonymous_And_Unprivileged_Callers()
    {
        var companyId = Guid.NewGuid();
        var (operationId, _) = await SeedTerminalInteractiveOperationAsync(companyId, Guid.NewGuid());

        using var anonymous = _factory.CreateClient();
        var anonymousResponse = await anonymous.PostAsJsonAsync(InteractiveResetUrl(companyId, operationId), new { reason = "x" });
        using var employee = await ClientAsync(EmployeeUser, SystemRoles.Employee, companyId);
        var employeeResponse = await employee.PostAsJsonAsync(InteractiveResetUrl(companyId, operationId), new { reason = "x" });

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, employeeResponse.StatusCode);
        var operation = await TasksAsync(db => db.TaskCompletionOperations.AsNoTracking().SingleAsync(o => o.Id == operationId));
        Assert.Equal(TaskCompletionOperation.StatusEffectsTerminalFailure, operation.Status);
    }

    private async Task<(Guid OperationId, Guid TaskId)> SeedTerminalInteractiveOperationAsync(Guid companyId, Guid employeeId)
    {
        var taskId = await TaskSeeder.SeedAsync(
            _factory, companyId, "Interactive task", assignedEmployeeId: employeeId, status: TaskItemStatus.Completed);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        var task = await db.TaskItems.AsNoTracking().SingleAsync(t => t.Id == taskId);
        var now = DateTimeOffset.UtcNow;

        var operation = TaskCompletionOperation.CreatePending(
            Guid.NewGuid(), companyId, taskId, task.CompletedBy ?? Guid.NewGuid(), null, null, now);
        operation.MarkDispatchApplied(now);
        operation.CaptureCompletionSnapshot(task.AssignedEmployeeId, task.Title, task.Description, "Open", task.CompletedAt ?? now, now);
        operation.MarkEffectsTerminalFailure(TaskCompletionOperation.CategoryEffectsRetryLimit, "Seeded retry-limit failure", now);
        db.TaskCompletionOperations.Add(operation);
        await db.SaveChangesAsync();
        return (operation.Id, taskId);
    }

    private sealed record ResetPayload(
        Guid OperationId, Guid? TaskId, bool WasReset, Guid? RecoveryActionId, int ResetCount, string? OperationKind);
}
