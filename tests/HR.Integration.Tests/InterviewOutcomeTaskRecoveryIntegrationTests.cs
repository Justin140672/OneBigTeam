using System.Net;
using System.Net.Http.Json;
using HR.Infrastructure.Abstractions;
using HR.Infrastructure.Persistence;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Notifications.Persistence;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.RecordInterviewOutcome;
using HR.Modules.Recruitment.Features.RetryInterviewOutcomeReconciliation;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Jobs;
using HR.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Cross-module PostgreSQL coverage of interview-outcome reconciliation: the real Recruitment
/// reconciliation service, TaskResolution/TaskCompleter, Tasks persistence and audit existence
/// confirmation, with only the audit persistence boundary fault-injected.
/// </summary>
[Collection("Integration")]
public class InterviewOutcomeTaskRecoveryIntegrationTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid OperatorUser = new("cc0000d1-0000-0000-0000-000000000001");

    public InterviewOutcomeTaskRecoveryIntegrationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(() => TestRoleSeeder.AssignRoleAsync(factory, OperatorUser, SystemRoles.CompanyAdministrator)).GetAwaiter().GetResult();
    }

    private async Task RecordOutcomeAsync(InterviewRecoverySeeder.Interview interview)
    {
        using var scope = _factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<InterviewOutcomeRecorder>().RecordAsync(
            new RecordInterviewOutcomeRequest
            {
                CompanyId = interview.CompanyId,
                VacancyId = interview.VacancyId,
                ApplicationId = interview.ApplicationId,
                InterviewId = interview.InterviewId,
                Outcome = InterviewOutcome.Passed,
            },
            Guid.NewGuid(), CancellationToken.None);
        Assert.True(result.IsSuccess);
    }

    private async Task<int> ReconcileAsync(InterviewRecoverySeeder.Interview interview)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<InterviewOutcomeTaskReconciliationService>()
            .RunOutstandingForInterviewAsync(interview.CompanyId, interview.InterviewId, CancellationToken.None);
    }

    private async Task<InterviewOutcomeTaskReconciliation> RecordRowAsync(Guid interviewId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>()
            .InterviewOutcomeTaskReconciliations.AsNoTracking().SingleAsync(r => r.InterviewId == interviewId);
    }

    private async Task<ProgrammaticTaskCompletion?> StateAsync(Guid taskId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TasksDbContext>()
            .ProgrammaticTaskCompletions.AsNoTracking().SingleOrDefaultAsync(c => c.TaskId == taskId);
    }

    private async Task<TaskItemStatus> TaskStatusAsync(Guid taskId)
    {
        using var scope = _factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<TasksDbContext>()
            .TaskItems.AsNoTracking().SingleAsync(t => t.Id == taskId)).Status;
    }

    private async Task<int> AuditCountAsync(Guid eventId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AuditDbContext>()
            .AuditEvents.CountAsync(e => e.EventId == eventId);
    }

    private async Task<int> CompletedNotificationCountAsync(Guid taskId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>()
            .Notifications.CountAsync(n => n.SourceEntityId == taskId && n.Type == NotificationType.TaskCompleted);
    }

    private async Task AssertFullyReconciledAsync(
        InterviewRecoverySeeder.Interview interview, InterviewRecoverySeeder.OutcomeTasks tasks)
    {
        Assert.Equal(TaskItemStatus.Completed, await TaskStatusAsync(tasks.CompleteTaskId));
        Assert.Equal(TaskItemStatus.Cancelled, await TaskStatusAsync(tasks.ReviewTaskId));
        Assert.NotNull((await StateAsync(tasks.CompleteTaskId))!.ConfirmedAt);
        Assert.Equal(1, await AuditCountAsync(interview.InterviewId));
        Assert.Equal(1, await AuditCountAsync(tasks.CompleteTaskId));
        Assert.Equal(1, await CompletedNotificationCountAsync(tasks.CompleteTaskId));

        var record = await RecordRowAsync(interview.InterviewId);
        Assert.NotNull(record.CompletedAt);
        Assert.NotNull(record.AuditDeliveredAt);
        Assert.False(record.IsBlocked);
    }

    [Fact]
    public async Task Recording_Outcome_Completes_The_Real_Feedback_Task_Cancels_Review_And_Confirms_Both_Audit_Events()
    {
        var interview = await InterviewRecoverySeeder.SeedInterviewAsync(_factory);
        var tasks = await InterviewRecoverySeeder.CreateOutcomeTasksAsync(_factory, interview);

        await RecordOutcomeAsync(interview);
        await ReconcileAsync(interview);

        await AssertFullyReconciledAsync(interview, tasks);
        Assert.Equal(1, _factory.AuditFaultInjector.PublishAttempts(interview.InterviewId));
        Assert.Equal(1, _factory.AuditFaultInjector.PublishAttempts(tasks.CompleteTaskId));
    }

    [Fact]
    public async Task Initially_Unavailable_Audit_Write_Leaves_Everything_Outstanding_Then_Reconciliation_Recovers()
    {
        var interview = await InterviewRecoverySeeder.SeedInterviewAsync(_factory);
        var tasks = await InterviewRecoverySeeder.CreateOutcomeTasksAsync(_factory, interview);
        _factory.AuditFaultInjector.Drop(interview.InterviewId, tasks.CompleteTaskId);

        try
        {
            await RecordOutcomeAsync(interview);
            await ReconcileAsync(interview);

            var outstanding = await RecordRowAsync(interview.InterviewId);
            Assert.Null(outstanding.CompletedAt);
            Assert.Null(outstanding.AuditDeliveredAt);
            Assert.False(outstanding.IsBlocked);
            var state = await StateAsync(tasks.CompleteTaskId);
            Assert.Null(state!.AuditPublishedAt);
            Assert.Null(state.ConfirmedAt);
            Assert.False(state.IsTerminallyFailed);
            Assert.Equal(0, await AuditCountAsync(interview.InterviewId));
            Assert.Equal(0, await AuditCountAsync(tasks.CompleteTaskId));
        }
        finally
        {
            _factory.AuditFaultInjector.Restore(interview.InterviewId, tasks.CompleteTaskId);
        }

        await ReconcileAsync(interview);
        await ReconcileAsync(interview);

        await AssertFullyReconciledAsync(interview, tasks);
    }

    [Fact]
    public async Task Programmatic_Completion_Reaching_Terminal_State_Blocks_Recruitment_And_Stops_Retrying()
    {
        var interview = await InterviewRecoverySeeder.SeedInterviewAsync(_factory);
        var tasks = await InterviewRecoverySeeder.CreateOutcomeTasksAsync(_factory, interview);
        _factory.AuditFaultInjector.Drop(tasks.CompleteTaskId);

        try
        {
            await RecordOutcomeAsync(interview);
            await DriveToBlockedAsync(interview);

            var record = await RecordRowAsync(interview.InterviewId);
            var state = await StateAsync(tasks.CompleteTaskId);
            Assert.True(record.IsBlocked);
            Assert.Equal(InterviewOutcomeTaskReconciliation.BlockedTaskTerminalFailure, record.BlockedCategory);
            Assert.Equal(state!.OperationId, record.BlockedTasksOperationId);
            Assert.Equal(tasks.CompleteTaskId, record.BlockedTaskId);
            Assert.True(state.IsTerminallyFailed);
            Assert.Null(record.ClaimedUntil);

            var attemptsBefore = (record.AttemptCount, state.AttemptCount);
            Assert.Equal(0, await ReconcileAsync(interview));
            Assert.Equal(0, await ReconcileAsync(interview));

            var after = await RecordRowAsync(interview.InterviewId);
            Assert.Equal(attemptsBefore, (after.AttemptCount, (await StateAsync(tasks.CompleteTaskId))!.AttemptCount));
        }
        finally
        {
            _factory.AuditFaultInjector.Restore(tasks.CompleteTaskId);
        }
    }

    [Fact]
    public async Task Operator_Reset_Then_Reconciliation_Recovers_End_To_End_Without_Repeating_Confirmed_Effects()
    {
        var interview = await InterviewRecoverySeeder.SeedInterviewAsync(_factory);
        var tasks = await InterviewRecoverySeeder.CreateOutcomeTasksAsync(_factory, interview);
        _factory.AuditFaultInjector.Drop(tasks.CompleteTaskId);

        try
        {
            await RecordOutcomeAsync(interview);
            await DriveToBlockedAsync(interview);
        }
        finally
        {
            _factory.AuditFaultInjector.Restore(tasks.CompleteTaskId);
        }

        var blocked = await RecordRowAsync(interview.InterviewId);
        var stateBefore = await StateAsync(tasks.CompleteTaskId);
        Assert.NotNull(stateBefore!.NotificationsClearedAt);
        Assert.NotNull(stateBefore.CompletionNotificationAt);
        Assert.Equal(1, await CompletedNotificationCountAsync(tasks.CompleteTaskId));

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, OperatorUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, interview.CompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, OperatorUser, SystemRoles.CompanyAdministrator, interview.CompanyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{interview.CompanyId}/recruitment/interview-outcome-reconciliations/{blocked.Id}/retry",
            new { reason = "Audit store recovered" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<RetryPayload>();
        Assert.Equal("completed", payload!.Status);
        Assert.True(payload.WasBlocked);
        Assert.True(payload.TasksCompletionReset);

        await AssertFullyReconciledAsync(interview, tasks);
        var stateAfter = await StateAsync(tasks.CompleteTaskId);
        Assert.Equal(1, stateAfter!.ResetCount);
        Assert.Equal(OperatorUser, stateAfter.LastResetBy);
        Assert.Equal(stateBefore.NotificationsClearedAt, stateAfter.NotificationsClearedAt);
        Assert.Equal(stateBefore.CompletionNotificationAt, stateAfter.CompletionNotificationAt);

        using var scope = _factory.Services.CreateScope();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        Assert.Equal(1, await auditDb.AuditEvents.CountAsync(e =>
            e.EventType == "task.completion_reset" && e.EntityId == tasks.CompleteTaskId && e.ActorUserId == OperatorUser));
        Assert.Equal(1, await auditDb.AuditEvents.CountAsync(e =>
            e.EventType == "interview.outcome_reconciliation_repaired" && e.EntityId == interview.InterviewId));
    }

    [Fact]
    public async Task Concurrent_Reconciliation_Workers_Produce_One_Final_Set_Of_Effects()
    {
        var interview = await InterviewRecoverySeeder.SeedInterviewAsync(_factory);
        var tasks = await InterviewRecoverySeeder.CreateOutcomeTasksAsync(_factory, interview);
        await RecordOutcomeAsync(interview);

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => ReconcileAsync(interview))));
        await ReconcileAsync(interview);

        await AssertFullyReconciledAsync(interview, tasks);
        Assert.Equal(1, _factory.AuditFaultInjector.PublishAttempts(interview.InterviewId));
        Assert.Equal(1, _factory.AuditFaultInjector.PublishAttempts(tasks.CompleteTaskId));
    }

    [Fact]
    public async Task Interactive_Completion_Does_Not_Become_Processed_While_Its_Audit_Event_Is_Absent()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee);
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee, companyId);
        var taskId = await TaskSeeder.SeedAsync(_factory, companyId, "Interactive task", assignedEmployeeId: userId);
        _factory.AuditFaultInjector.Drop(taskId);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        try
        {
            var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/tasks/{taskId}/complete", new { });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var scope = _factory.Services.CreateScope();
            var tasksDb = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
            var operation = await tasksDb.TaskCompletionOperations.AsNoTracking().SingleAsync(o => o.TaskId == taskId);
            Assert.Equal(TaskItemStatus.Completed, await TaskStatusAsync(taskId));
            Assert.Equal(TaskCompletionOperation.StatusDispatchApplied, operation.Status);
            Assert.Equal(0, await AuditCountAsync(taskId));
        }
        finally
        {
            _factory.AuditFaultInjector.Restore(taskId);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var tasksDb = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
            var operationId = (await tasksDb.TaskCompletionOperations.AsNoTracking().SingleAsync(o => o.TaskId == taskId)).Id;
            await scope.ServiceProvider.GetRequiredService<TaskCompletionEffectsJob>().ProcessAsync(operationId, companyId);
        }

        using var verify = _factory.Services.CreateScope();
        var finalOperation = await verify.ServiceProvider.GetRequiredService<TasksDbContext>()
            .TaskCompletionOperations.AsNoTracking().SingleAsync(o => o.TaskId == taskId);
        Assert.Equal(TaskCompletionOperation.StatusProcessed, finalOperation.Status);
        Assert.Equal(1, await AuditCountAsync(taskId));
        Assert.Equal(2, _factory.AuditFaultInjector.PublishAttempts(taskId));
    }

    private async Task DriveToBlockedAsync(InterviewRecoverySeeder.Interview interview)
    {
        for (var i = 0; i < 15; i++)
        {
            await ReconcileAsync(interview);

            if ((await RecordRowAsync(interview.InterviewId)).IsBlocked)
                return;
        }

        Assert.Fail("The reconciliation never reached the blocked state.");
    }

    private sealed record RetryPayload(Guid ReconciliationId, Guid InterviewId, string Status, bool WasBlocked, bool TasksCompletionReset);
}
