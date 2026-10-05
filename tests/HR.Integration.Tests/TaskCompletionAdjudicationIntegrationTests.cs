using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Infrastructure.Abstractions;
using HR.Infrastructure.Persistence;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Notifications.Persistence;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Jobs;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Tasks;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Jobs;
using HR.Modules.Tasks.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class TaskCompletionAdjudicationIntegrationTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid OperatorUser = new("cc0000d4-0000-0000-0000-000000000001");
    private static readonly Guid EmployeeUser = new("cc0000d4-0000-0000-0000-000000000002");

    public TaskCompletionAdjudicationIntegrationTests(ApiWebApplicationFactory factory)
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

    private static string AdjudicateUrl(Guid companyId, Guid operationId) =>
        $"/api/companies/{companyId}/tasks/completion-operations/{operationId}/adjudicate";

    private static string ResetUrl(Guid companyId, Guid operationId) =>
        $"/api/companies/{companyId}/tasks/completion-operations/{operationId}/reset";

    private static string ListUrl(Guid companyId, string query = "") =>
        $"/api/companies/{companyId}/tasks/completion-operations/requiring-intervention{query}";

    private static object Evidence(Guid? employeeId, bool notificationRequired = true) => new
    {
        notificationRequired,
        assignedEmployeeId = employeeId,
        completedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        previousTaskStatus = "Open",
        taskTitle = "Record feedback",
    };

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

    private Task<TaskCompletionOperation> OperationAsync(Guid operationId) =>
        TasksAsync(db => db.TaskCompletionOperations.AsNoTracking().SingleAsync(o => o.Id == operationId));

    private Task<List<TaskRecoveryAction>> ActionsAsync(Guid operationId) =>
        TasksAsync(db => db.TaskRecoveryActions.AsNoTracking().Where(a => a.OperationId == operationId).OrderBy(a => a.SequenceNumber).ToListAsync());

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

    private async Task RunEffectsJobAsync(Guid operationId, Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TaskCompletionEffectsJob>().ProcessAsync(operationId, companyId);
    }

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

    private async Task PublishCompletionAuditAsync(Guid companyId, Guid taskId, Guid? employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAuditEventPublisher>().PublishAsync(
            new TaskCompletedAuditEvent(companyId, taskId, Guid.NewGuid(), "Open", employeeId, DateTimeOffset.UtcNow),
            CancellationToken.None);
    }

    private async Task WriteCompletionNotificationAsync(Guid companyId, Guid taskId, Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<INotificationWriter>().WriteAsync(
            Guid.NewGuid(), companyId, employeeId, "Task completed: Record feedback", null, taskId,
            NotificationType.TaskCompleted, NotificationPriority.Normal, DateTimeOffset.UtcNow);
    }

    private async Task<(Guid OperationId, Guid TaskId)> SeedOperationAsync(
        Guid companyId, string status, Guid? employeeId = null, bool taskExists = true, bool snapshot = false,
        Guid? sourceEntityId = null, TaskSource source = TaskSource.Workflow, TaskActionType actionType = TaskActionType.Complete)
    {
        var taskId = taskExists
            ? await TaskSeeder.SeedAsync(
                _factory, companyId, "Record feedback", "private description", source: source, actionType: actionType,
                assignedEmployeeId: employeeId, sourceEntityId: sourceEntityId, status: TaskItemStatus.Completed)
            : Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        var now = DateTimeOffset.UtcNow;
        var operation = TaskCompletionOperation.CreatePending(Guid.NewGuid(), companyId, taskId, Guid.NewGuid(), null, null, now);
        if (status != TaskCompletionOperation.StatusPending)
            operation.MarkDispatchApplied(now);
        if (snapshot)
            operation.CaptureCompletionSnapshot(employeeId, "Record feedback", null, "Open", now, now);

        switch (status)
        {
            case TaskCompletionOperation.StatusProcessed:
                operation.MarkProcessed(now);
                break;
            case TaskCompletionOperation.StatusEffectsTerminalFailure:
                operation.MarkEffectsTerminalFailure(TaskCompletionOperation.CategoryEffectsRetryLimit, "secret exception text", now);
                break;
            case TaskCompletionOperation.StatusDataIntegrityFailure:
                operation.MarkDataIntegrityFailure("secret exception text", now, TaskCompletionOperation.CategoryEvidenceMissing);
                break;
        }

        db.TaskCompletionOperations.Add(operation);
        await db.SaveChangesAsync();
        return (operation.Id, taskId);
    }

    private async Task<HttpClient> EmployeeClientAsync(Guid companyId, Guid employeeId)
    {
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee);
        return await ClientAsync(employeeId, SystemRoles.Employee, companyId);
    }

    [Theory]
    [InlineData(TaskCompletionOperation.StatusEffectsTerminalFailure, "conflict.effects_terminal_failure", true, "reset")]
    [InlineData(TaskCompletionOperation.StatusDataIntegrityFailure, "conflict.data_integrity_failure", false, "adjudicate")]
    public async Task Completion_Retry_Of_A_Terminal_Operation_Returns_A_Typed_Conflict_Without_Sensitive_Details(
        string status, string code, bool resettable, string recoveryAction)
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var (operationId, taskId) = await SeedOperationAsync(companyId, status, employeeId);
        using var client = await EmployeeClientAsync(companyId, employeeId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/tasks/{taskId}/complete", new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(raw);
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        var details = json.RootElement.GetProperty("details");
        Assert.Equal(operationId, details.GetProperty("operationId").GetGuid());
        Assert.Equal(taskId, details.GetProperty("taskId").GetGuid());
        Assert.Equal(status, details.GetProperty("status").GetString());
        Assert.Equal(resettable, details.GetProperty("resettable").GetBoolean());
        Assert.Equal(recoveryAction, details.GetProperty("recoveryAction").GetString());
        Assert.DoesNotContain("secret exception text", raw);
        Assert.DoesNotContain("private description", raw);
    }

    [Theory]
    [InlineData(TaskCompletionOperation.StatusProcessed, "confirmed")]
    [InlineData(TaskCompletionOperation.StatusDispatchApplied, "pending")]
    public async Task Completion_Retry_Reports_Confirmed_Or_Pending_Effects_Without_Failing(string status, string effects)
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var (operationId, taskId) = await SeedOperationAsync(companyId, status, employeeId);
        using var client = await EmployeeClientAsync(companyId, employeeId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/tasks/{taskId}/complete", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(effects, json.RootElement.GetProperty("effectsStatus").GetString());
        Assert.Equal(status, (await OperationAsync(operationId)).Status);
    }

    [Fact]
    public async Task Adjudication_Requires_Authentication_Company_Management_And_Tenant_Isolation()
    {
        var companyId = Guid.NewGuid();
        var (operationId, _) = await SeedOperationAsync(companyId, TaskCompletionOperation.StatusDataIntegrityFailure, taskExists: false);
        var body = new { resolution = "waived", reason = "x" };

        using var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(AdjudicateUrl(companyId, operationId), body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(ListUrl(companyId))).StatusCode);

        using var employee = await ClientAsync(EmployeeUser, SystemRoles.Employee, companyId);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PostAsJsonAsync(AdjudicateUrl(companyId, operationId), body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync(ListUrl(companyId))).StatusCode);

        var otherCompany = Guid.NewGuid();
        using var foreign = await ClientAsync(OperatorUser, SystemRoles.CompanyAdministrator, otherCompany);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.PostAsJsonAsync(AdjudicateUrl(otherCompany, operationId), body)).StatusCode);

        var foreignList = await foreign.GetFromJsonAsync<ListPayload>(ListUrl(otherCompany));
        Assert.Empty(foreignList!.Items);
        Assert.Equal(TaskCompletionOperation.StatusDataIntegrityFailure, (await OperationAsync(operationId)).Status);
        Assert.Empty(await ActionsAsync(operationId));
    }

    [Fact]
    public async Task Adjudication_Validates_The_Request()
    {
        var companyId = Guid.NewGuid();
        var (operationId, _) = await SeedOperationAsync(companyId, TaskCompletionOperation.StatusDataIntegrityFailure, taskExists: false);
        using var client = await ClientAsync(OperatorUser, SystemRoles.CompanyAdministrator, companyId);

        var unknown = await client.PostAsJsonAsync(AdjudicateUrl(companyId, operationId), new { resolution = "magic", reason = "x" });
        var noReason = await client.PostAsJsonAsync(AdjudicateUrl(companyId, operationId), new { resolution = "waived", reason = "" });
        var noEvidence = await client.PostAsJsonAsync(AdjudicateUrl(companyId, operationId), new { resolution = "evidence_retry", reason = "x" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noReason.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noEvidence.StatusCode);
        Assert.Equal(TaskCompletionOperation.StatusDataIntegrityFailure, (await OperationAsync(operationId)).Status);
    }

    [Fact]
    public async Task The_Standard_Reset_Endpoint_Still_Rejects_A_Data_Integrity_Failure()
    {
        var companyId = Guid.NewGuid();
        var (operationId, _) = await SeedOperationAsync(companyId, TaskCompletionOperation.StatusDataIntegrityFailure, taskExists: false);
        using var client = await ClientAsync(OperatorUser, SystemRoles.CompanyAdministrator, companyId);

        var response = await client.PostAsJsonAsync(ResetUrl(companyId, operationId), new { reason = "try" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(TaskCompletionOperation.StatusDataIntegrityFailure, (await OperationAsync(operationId)).Status);
        Assert.Empty(await ActionsAsync(operationId));
    }

    [Fact]
    public async Task Evidence_And_Retry_Converges_To_Processed_Without_Duplicating_Existing_Effects()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var (operationId, taskId) = await SeedOperationAsync(companyId, TaskCompletionOperation.StatusDataIntegrityFailure, taskExists: false);
        await PublishCompletionAuditAsync(companyId, taskId, employeeId);
        using var client = await ClientAsync(OperatorUser, SystemRoles.CompanyAdministrator, companyId);

        var response = await client.PostAsJsonAsync(
            AdjudicateUrl(companyId, operationId),
            new { resolution = "evidence_retry", reason = "Evidence from the support ticket", evidence = Evidence(employeeId) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AdjudicationPayload>();
        Assert.True(payload!.WasApplied);
        Assert.Equal(TaskCompletionOperation.StatusDispatchApplied, payload.Status);

        await RunEffectsJobAsync(operationId, companyId);
        await RunEffectsJobAsync(operationId, companyId);

        var operation = await OperationAsync(operationId);
        Assert.Equal(TaskCompletionOperation.StatusProcessed, operation.Status);
        Assert.Equal(1, await AuditCountAsync(taskId));
        Assert.Equal(1, await CompletedNotificationCountAsync(taskId));

        var repeat = await client.PostAsJsonAsync(
            AdjudicateUrl(companyId, operationId),
            new { resolution = "evidence_retry", reason = "Evidence from the support ticket", evidence = Evidence(employeeId) });
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Assert.False((await repeat.Content.ReadFromJsonAsync<AdjudicationPayload>())!.WasApplied);
        Assert.Single(await ActionsAsync(operationId));
        var action = Assert.Single(await ActionsAsync(operationId));
        Assert.NotNull(action.AuditDeliveredAt);
        Assert.Equal(1, await AuditCountAsync(action.Id));
    }

    [Fact]
    public async Task Evidence_Retry_Does_Not_Duplicate_An_Already_Written_Notification()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var (operationId, taskId) = await SeedOperationAsync(companyId, TaskCompletionOperation.StatusDataIntegrityFailure, taskExists: false);
        await WriteCompletionNotificationAsync(companyId, taskId, employeeId);
        using var client = await ClientAsync(OperatorUser, SystemRoles.CompanyAdministrator, companyId);

        await client.PostAsJsonAsync(
            AdjudicateUrl(companyId, operationId),
            new { resolution = "evidence_retry", reason = "Evidence", evidence = Evidence(employeeId) });
        await RunEffectsJobAsync(operationId, companyId);

        Assert.Equal(TaskCompletionOperation.StatusProcessed, (await OperationAsync(operationId)).Status);
        Assert.Equal(1, await CompletedNotificationCountAsync(taskId));
        Assert.Equal(1, await AuditCountAsync(taskId));
    }

    [Fact]
    public async Task Verified_And_Waived_Are_Distinct_From_Processed_And_Are_Listed_By_The_Management_Query()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var (verifiedId, verifiedTask) = await SeedOperationAsync(companyId, TaskCompletionOperation.StatusDataIntegrityFailure, taskExists: false);
        var (waivedId, _) = await SeedOperationAsync(companyId, TaskCompletionOperation.StatusDataIntegrityFailure, taskExists: false);
        using var client = await ClientAsync(OperatorUser, SystemRoles.CompanyAdministrator, companyId);

        var rejected = await client.PostAsJsonAsync(
            AdjudicateUrl(companyId, verifiedId),
            new { resolution = "effects_verified", reason = "Checked", evidence = Evidence(employeeId) });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);

        await PublishCompletionAuditAsync(companyId, verifiedTask, employeeId);
        await WriteCompletionNotificationAsync(companyId, verifiedTask, employeeId);
        var verified = await client.PostAsJsonAsync(
            AdjudicateUrl(companyId, verifiedId),
            new { resolution = "effects_verified", reason = "Checked", evidence = Evidence(employeeId) });
        var waived = await client.PostAsJsonAsync(
            AdjudicateUrl(companyId, waivedId), new { resolution = "waived", reason = "Unrecoverable legacy data" });

        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        Assert.Equal(HttpStatusCode.OK, waived.StatusCode);
        Assert.Equal(TaskCompletionOperation.StatusEffectsVerified, (await OperationAsync(verifiedId)).Status);
        Assert.Equal(TaskCompletionOperation.StatusWaived, (await OperationAsync(waivedId)).Status);
        Assert.Null((await OperationAsync(waivedId)).ProcessedAt);

        var verifiedList = await client.GetFromJsonAsync<ListPayload>(ListUrl(companyId, "?status=effects_verified"));
        Assert.Equal([verifiedId], verifiedList!.Items.Select(i => i.OperationId));
        Assert.Equal("adjudicated_verified", verifiedList.Items[0].RecoveryStatus);
        var waivedList = await client.GetFromJsonAsync<ListPayload>(ListUrl(companyId, "?status=waived"));
        Assert.Equal([waivedId], waivedList!.Items.Select(i => i.OperationId));
        var defaultList = await client.GetFromJsonAsync<ListPayload>(ListUrl(companyId));
        Assert.Empty(defaultList!.Items);
    }

    [Fact]
    public async Task Management_Query_Lists_Filters_Orders_And_Paginates_Terminal_Operations_Of_The_Own_Company_Only()
    {
        var companyId = Guid.NewGuid();
        var terminal = await SeedOperationAsync(companyId, TaskCompletionOperation.StatusEffectsTerminalFailure, taskExists: false);
        await Task.Delay(20);
        var integrity = await SeedOperationAsync(companyId, TaskCompletionOperation.StatusDataIntegrityFailure, taskExists: false);
        await SeedOperationAsync(companyId, TaskCompletionOperation.StatusProcessed, taskExists: false);
        await SeedOperationAsync(Guid.NewGuid(), TaskCompletionOperation.StatusEffectsTerminalFailure, taskExists: false);
        using var client = await ClientAsync(OperatorUser, SystemRoles.CompanyAdministrator, companyId);

        var all = await client.GetFromJsonAsync<ListPayload>(ListUrl(companyId));
        Assert.Equal(2, all!.TotalCount);
        Assert.Equal([terminal.OperationId, integrity.OperationId], all.Items.Select(i => i.OperationId));
        Assert.True(all.Items[0].IsResettable);
        Assert.True(all.Items[1].RequiresInvestigation);

        var byStatus = await client.GetFromJsonAsync<ListPayload>(ListUrl(companyId, "?status=data_integrity_failure"));
        Assert.Equal([integrity.OperationId], byStatus!.Items.Select(i => i.OperationId));
        var byCategory = await client.GetFromJsonAsync<ListPayload>(ListUrl(companyId, "?failureCategory=effects_retry_limit"));
        Assert.Equal([terminal.OperationId], byCategory!.Items.Select(i => i.OperationId));
        var byTask = await client.GetFromJsonAsync<ListPayload>(ListUrl(companyId, $"?taskId={integrity.TaskId}"));
        Assert.Equal([integrity.OperationId], byTask!.Items.Select(i => i.OperationId));
        var byOperation = await client.GetFromJsonAsync<ListPayload>(ListUrl(companyId, $"?operationId={terminal.OperationId}"));
        Assert.Equal([terminal.OperationId], byOperation!.Items.Select(i => i.OperationId));
        var page = await client.GetFromJsonAsync<ListPayload>(ListUrl(companyId, "?pageNumber=2&pageSize=1"));
        Assert.Equal([integrity.OperationId], page!.Items.Select(i => i.OperationId));

        var badStatus = await client.GetAsync(ListUrl(companyId, "?status=processed"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badStatus.StatusCode);
    }

    [Fact]
    public async Task Adjudication_Audit_Delivery_Is_Retried_With_The_Same_Event_Id_Without_Repeating_The_Transition()
    {
        var companyId = Guid.NewGuid();
        var (operationId, _) = await SeedOperationAsync(companyId, TaskCompletionOperation.StatusDataIntegrityFailure, taskExists: false);
        var eventId = TaskRecoveryAction.AdjudicationEventIdFor(operationId, 1);
        using var client = await ClientAsync(OperatorUser, SystemRoles.CompanyAdministrator, companyId);
        _factory.AuditFaultInjector.Drop(eventId);

        try
        {
            var response = await client.PostAsJsonAsync(
                AdjudicateUrl(companyId, operationId), new { resolution = "waived", reason = "Audit outage" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var pending = Assert.Single(await ActionsAsync(operationId));
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

        Assert.NotNull(Assert.Single(await ActionsAsync(operationId)).AuditDeliveredAt);
        Assert.Equal(1, await AuditCountAsync(eventId));
        Assert.Equal(1, (await OperationAsync(operationId)).AdjudicationCount);
    }

    [Fact]
    public async Task Concurrent_Adjudications_Have_One_Winner_And_One_Durable_Action()
    {
        var companyId = Guid.NewGuid();
        var (operationId, _) = await SeedOperationAsync(companyId, TaskCompletionOperation.StatusDataIntegrityFailure, taskExists: false);
        using var first = await ClientAsync(OperatorUser, SystemRoles.CompanyAdministrator, companyId);
        using var second = await ClientAsync(OperatorUser, SystemRoles.CompanyAdministrator, companyId);

        var responses = await Task.WhenAll(
            Task.Run(() => first.PostAsJsonAsync(AdjudicateUrl(companyId, operationId), new { resolution = "waived", reason = "a" })),
            Task.Run(() => second.PostAsJsonAsync(
                AdjudicateUrl(companyId, operationId),
                new { resolution = "evidence_retry", reason = "b", evidence = Evidence(Guid.NewGuid()) })));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Single(await ActionsAsync(operationId));
        Assert.Equal(1, (await OperationAsync(operationId)).AdjudicationCount);
    }

    private async Task<(Guid CompanyId, Guid InterviewId, Guid ReconciliationId, Guid OperationId, Guid TaskId, Guid EmployeeId)>
        SeedBlockedRecruitmentAsync()
    {
        var interview = await InterviewRecoverySeeder.SeedInterviewAsync(_factory);
        var employeeId = Guid.NewGuid();
        var (operationId, taskId) = await SeedOperationAsync(
            interview.CompanyId, TaskCompletionOperation.StatusDataIntegrityFailure, employeeId,
            sourceEntityId: interview.InterviewId, source: TaskSource.Recruitment, actionType: TaskActionType.Complete);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var record = InterviewOutcomeTaskReconciliation.Create(
            Guid.NewGuid(), interview.CompanyId, interview.ApplicationId, interview.InterviewId, Guid.NewGuid(), DateTimeOffset.UtcNow);
        record.Block(
            InterviewOutcomeTaskReconciliation.BlockedTaskTerminalFailure, "Seeded data-integrity failure", taskId, operationId,
            DateTimeOffset.UtcNow);
        db.InterviewOutcomeTaskReconciliations.Add(record);
        await db.SaveChangesAsync();
        return (interview.CompanyId, interview.InterviewId, record.Id, operationId, taskId, employeeId);
    }

    private Task<InterviewOutcomeTaskReconciliation> ReconciliationAsync(Guid id) =>
        RecruitmentAsync(db => db.InterviewOutcomeTaskReconciliations.AsNoTracking().SingleAsync(r => r.Id == id));

    private async Task<int> ProgrammaticStateCountAsync(Guid taskId) =>
        await TasksAsync(db => db.ProgrammaticTaskCompletions.CountAsync(c => c.TaskId == taskId));

    [Fact]
    public async Task Recruitment_Stays_Blocked_While_The_Tasks_Operation_Requires_Investigation()
    {
        var seeded = await SeedBlockedRecruitmentAsync();

        await RunRecruitmentSweepAsync();
        await RunRecruitmentSweepAsync();

        var record = await ReconciliationAsync(seeded.ReconciliationId);
        Assert.True(record.IsBlocked);
        Assert.Equal(0, record.RepairCount);
        Assert.Equal(0, await ProgrammaticStateCountAsync(seeded.TaskId));
    }

    [Fact]
    public async Task Recruitment_Unblocks_After_Evidence_And_Retry_And_Completes_Once_Effects_Are_Confirmed()
    {
        var seeded = await SeedBlockedRecruitmentAsync();
        using var client = await ClientAsync(OperatorUser, SystemRoles.CompanyAdministrator, seeded.CompanyId);

        var response = await client.PostAsJsonAsync(
            AdjudicateUrl(seeded.CompanyId, seeded.OperationId),
            new { resolution = "evidence_retry", reason = "Evidence supplied", evidence = Evidence(seeded.EmployeeId) });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await RunRecruitmentSweepAsync();

        var unblocked = await ReconciliationAsync(seeded.ReconciliationId);
        Assert.False(unblocked.IsBlocked);
        Assert.Null(unblocked.CompletedAt);
        Assert.Equal(OperatorUser, unblocked.LastRepairedBy);

        await RunEffectsJobAsync(seeded.OperationId, seeded.CompanyId);
        await RunRecruitmentSweepAsync();
        await RunRecruitmentSweepAsync();

        var record = await ReconciliationAsync(seeded.ReconciliationId);
        Assert.NotNull(record.CompletedAt);
        Assert.False(record.IsWaived);
        Assert.Equal(1, record.RepairCount);
        Assert.Equal(TaskCompletionOperation.StatusProcessed, (await OperationAsync(seeded.OperationId)).Status);
        Assert.Equal(1, await AuditCountAsync(seeded.TaskId));
        Assert.Equal(1, await AuditCountAsync(seeded.InterviewId));
        Assert.Equal(1, await CompletedNotificationCountAsync(seeded.TaskId));
        Assert.Equal(0, await ProgrammaticStateCountAsync(seeded.TaskId));
        var repair = await RecruitmentAsync(db => db.InterviewOutcomeRepairActions.AsNoTracking()
            .SingleAsync(a => a.ReconciliationId == seeded.ReconciliationId));
        Assert.Equal(InterviewOutcomeRepairAction.SourceTasksAdjudication, repair.Source);
        Assert.NotNull(repair.AuditDeliveredAt);
        Assert.Equal(1, await AuditCountAsync(repair.Id));
    }

    [Fact]
    public async Task Recruitment_Treats_A_Verified_Resolution_As_Confirmed_And_Completes_Without_A_Waiver_Marker()
    {
        var seeded = await SeedBlockedRecruitmentAsync();
        await PublishCompletionAuditAsync(seeded.CompanyId, seeded.TaskId, seeded.EmployeeId);
        await WriteCompletionNotificationAsync(seeded.CompanyId, seeded.TaskId, seeded.EmployeeId);
        using var client = await ClientAsync(OperatorUser, SystemRoles.CompanyAdministrator, seeded.CompanyId);

        var response = await client.PostAsJsonAsync(
            AdjudicateUrl(seeded.CompanyId, seeded.OperationId),
            new { resolution = "effects_verified", reason = "Verified", evidence = Evidence(seeded.EmployeeId) });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await RunRecruitmentSweepAsync();
        await RunRecruitmentSweepAsync();

        var record = await ReconciliationAsync(seeded.ReconciliationId);
        Assert.False(record.IsBlocked);
        Assert.NotNull(record.CompletedAt);
        Assert.False(record.IsWaived);
        Assert.Equal(TaskCompletionOperation.StatusEffectsVerified, (await OperationAsync(seeded.OperationId)).Status);
        Assert.Equal(1, await AuditCountAsync(seeded.TaskId));
        Assert.Equal(1, await CompletedNotificationCountAsync(seeded.TaskId));
        Assert.Equal(0, await ProgrammaticStateCountAsync(seeded.TaskId));
    }

    [Fact]
    public async Task Recruitment_Closes_The_Reconciliation_As_Waived_When_Tasks_Is_Waived_And_Never_As_Plain_Completed()
    {
        var seeded = await SeedBlockedRecruitmentAsync();
        using var client = await ClientAsync(OperatorUser, SystemRoles.CompanyAdministrator, seeded.CompanyId);

        var response = await client.PostAsJsonAsync(
            AdjudicateUrl(seeded.CompanyId, seeded.OperationId),
            new { resolution = "waived", reason = "Unrecoverable legacy data" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await RunRecruitmentSweepAsync();
        await RunRecruitmentSweepAsync();

        var record = await ReconciliationAsync(seeded.ReconciliationId);
        Assert.False(record.IsBlocked);
        Assert.True(record.IsWaived);
        Assert.Equal(seeded.OperationId, record.WaivedTasksOperationId);
        Assert.NotNull(record.CompletedAt);
        Assert.Equal(TaskCompletionOperation.StatusWaived, (await OperationAsync(seeded.OperationId)).Status);
        Assert.Equal(0, await AuditCountAsync(seeded.TaskId));
        Assert.Equal(0, await CompletedNotificationCountAsync(seeded.TaskId));
        Assert.Equal(1, await AuditCountAsync(seeded.InterviewId));
        Assert.Equal(0, await ProgrammaticStateCountAsync(seeded.TaskId));

        using var retryClient = await ClientAsync(OperatorUser, SystemRoles.CompanyAdministrator, seeded.CompanyId);
        var retry = await retryClient.PostAsJsonAsync(
            $"/api/companies/{seeded.CompanyId}/recruitment/interview-outcome-reconciliations/{seeded.ReconciliationId}/retry",
            new { reason = "check" });
        using var json = JsonDocument.Parse(await retry.Content.ReadAsStringAsync());
        Assert.Equal("completed_waived", json.RootElement.GetProperty("status").GetString());
    }

    private sealed record AdjudicationPayload(
        Guid OperationId, Guid? TaskId, string? Status, string? ResolutionType, bool WasApplied,
        Guid? RecoveryActionId, int AdjudicationCount);

    private sealed record ListItem(
        Guid OperationId, Guid TaskId, string Status, string? FailureCategory, bool IsResettable,
        bool RequiresInvestigation, string RecoveryStatus);

    private sealed record ListPayload(List<ListItem> Items, int TotalCount, int PageNumber, int PageSize);
}
