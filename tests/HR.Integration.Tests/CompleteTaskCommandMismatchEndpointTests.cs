using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Infrastructure.Abstractions;
using HR.Infrastructure.Persistence;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Notifications.Persistence;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class CompleteTaskCommandMismatchEndpointTests(ApiWebApplicationFactory factory)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    private const string MismatchCode = "conflict.task_completion_command_mismatch";
    private const string PrivateDescription = "private description for mismatch";
    private const string PrivateTitle = "Mismatch private title";

    private sealed class BlockingWorkflowAction : ITaskCompletionAction
    {
        private int _calls;
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<TaskCompletionContext> _contexts = [];

        public TaskSource Source => TaskSource.Workflow;
        public TaskActionType ActionType => TaskActionType.Complete;
        public int Calls => _calls;
        public Task Entered => _entered.Task;
        public IReadOnlyList<TaskCompletionContext> Contexts { get { lock (_contexts) return _contexts.ToList(); } }

        public void Release() => _release.TrySetResult();

        public async Task<Result> ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            lock (_contexts)
                _contexts.Add(context);
            _entered.TrySetResult();
            await _release.Task.WaitAsync(Timeout, cancellationToken);
            return Result.Success();
        }
    }

    private sealed class Scenario(
        WebApplicationFactory<Program> appFactory,
        BlockingWorkflowAction action,
        Guid companyId,
        Guid employeeId,
        Guid taskId) : IDisposable
    {
        public Guid CompanyId => companyId;
        public Guid EmployeeId => employeeId;
        public Guid TaskId => taskId;
        public BlockingWorkflowAction Action => action;

        public Task<HttpResponseMessage> SendAsync(string? key, string? decision, string? reason, Guid? userId = null)
        {
            var client = appFactory.CreateClient();
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, (userId ?? employeeId).ToString());
            client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
            var request = new HttpRequestMessage(HttpMethod.Post, $"/api/companies/{companyId}/tasks/{taskId}/complete")
            {
                Content = JsonContent.Create(new { outcomeDecision = decision, outcomeReason = reason }),
            };
            if (key is not null)
                request.Headers.Add("Idempotency-Key", key);
            return client.SendAsync(request);
        }

        public void Dispose()
        {
            action.Release();
            appFactory.Dispose();
        }
    }

    private async Task<Scenario> NewScenarioAsync()
    {
        var action = new BlockingWorkflowAction();
        var appFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<ITaskCompletionAction>(action)));

        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var taskId = await TaskSeeder.SeedAsync(
            factory, companyId, PrivateTitle, PrivateDescription, assignedEmployeeId: employeeId);
        await TestRoleSeeder.AssignRoleAsync(factory, employeeId, SystemRoles.Employee, companyId);

        return new Scenario(appFactory, action, companyId, employeeId, taskId);
    }

    private async Task<T> TasksAsync<T>(Func<TasksDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<TasksDbContext>());
    }

    private Task<List<TaskCompletionOperation>> ActiveOperationsAsync(Scenario scenario) =>
        TasksAsync(db => db.TaskCompletionOperations.AsNoTracking()
            .Where(o => o.TaskId == scenario.TaskId && o.Status != TaskCompletionOperation.StatusRejected)
            .ToListAsync());

    private static async Task<(HttpStatusCode Status, string Raw, JsonElement Json)> ReadAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        return (response.StatusCode, raw, doc.RootElement.Clone());
    }

    private static void AssertSafeMismatch(
        (HttpStatusCode Status, string Raw, JsonElement Json) response,
        Guid taskId,
        Guid operationId,
        params string[] forbidden)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.Status);
        Assert.Equal(MismatchCode, response.Json.GetProperty("code").GetString());
        var details = response.Json.GetProperty("details");
        Assert.Equal(taskId, details.GetProperty("taskId").GetGuid());
        Assert.Equal(operationId, details.GetProperty("operationId").GetGuid());
        Assert.True(details.GetProperty("refresh").GetBoolean());
        Assert.False(response.Json.TryGetProperty("effectsStatus", out _));
        Assert.False(response.Json.TryGetProperty("completedBy", out _));

        foreach (var value in forbidden.Append(PrivateDescription).Append(PrivateTitle))
            Assert.DoesNotContain(value, response.Raw, StringComparison.OrdinalIgnoreCase);
    }

    private async Task AssertSingleCompletionEffectsAsync(Scenario scenario)
    {
        using var scope = factory.Services.CreateScope();
        var notifications = await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>()
            .Notifications.CountAsync(n => n.SourceEntityId == scenario.TaskId && n.Type == NotificationType.TaskCompleted);
        var audits = await scope.ServiceProvider.GetRequiredService<AuditDbContext>()
            .AuditEvents.CountAsync(e => e.EventId == scenario.TaskId);
        Assert.Equal(1, notifications);
        Assert.Equal(1, audits);
    }

    private static void AssertOperationUnchanged(TaskCompletionOperation before, TaskCompletionOperation after)
    {
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.CompletedBy, after.CompletedBy);
        Assert.Equal(before.OutcomeDecision, after.OutcomeDecision);
        Assert.Equal(before.OutcomeReason, after.OutcomeReason);
        Assert.Equal(before.CommandFingerprint, after.CommandFingerprint);
        Assert.Equal(before.CorrelationId, after.CorrelationId);
        Assert.Equal(before.CausationId, after.CausationId);
        Assert.Equal(before.MessageId, after.MessageId);
        Assert.Equal(before.CreatedAt, after.CreatedAt);
    }

    [Theory]
    [InlineData("key-a", "key-b")]
    [InlineData(null, null)]
    [InlineData("key-a", null)]
    [InlineData(null, "key-b")]
    public async Task Conflicting_Decision_Loses_With_Mismatch_And_Only_The_Winner_Is_Dispatched(string? keyA, string? keyB)
    {
        using var scenario = await NewScenarioAsync();

        var requestA = scenario.SendAsync(keyA, "Approve", "winner reason");
        await scenario.Action.Entered.WaitAsync(Timeout);
        var claimed = Assert.Single(await ActiveOperationsAsync(scenario));

        var loser = await ReadAsync(await scenario.SendAsync(keyB, "Reject", "loser reason").WaitAsync(Timeout));

        AssertSafeMismatch(loser, scenario.TaskId, claimed.Id, "Reject", "Approve", "winner reason", "loser reason");
        Assert.Equal(1, scenario.Action.Calls);
        AssertOperationUnchanged(claimed, Assert.Single(await ActiveOperationsAsync(scenario)));

        scenario.Action.Release();
        var winner = await ReadAsync(await requestA.WaitAsync(Timeout));
        Assert.Equal(HttpStatusCode.OK, winner.Status);

        var operation = Assert.Single(await ActiveOperationsAsync(scenario));
        AssertOperationUnchanged(claimed, operation);
        Assert.Equal("Approve", operation.OutcomeDecision);
        Assert.Equal("winner reason", operation.OutcomeReason);
        Assert.Equal(scenario.EmployeeId, operation.CompletedBy);

        var context = Assert.Single(scenario.Action.Contexts);
        Assert.Equal("Approve", context.OutcomeDecision);
        Assert.Equal("winner reason", context.OutcomeReason);
        Assert.Equal(operation.Id, context.DispatchOperationId);
        Assert.Equal(1, scenario.Action.Calls);

        await AssertSingleCompletionEffectsAsync(scenario);

        if (keyB is not null)
        {
            Assert.Equal(0, await TasksAsync(db => db.IdempotencyRecords
                .CountAsync(r => r.CompanyId == scenario.CompanyId && r.Key == keyB)));
        }

        Assert.Equal(keyA is null ? 0 : 1,
            await TasksAsync(db => db.IdempotencyRecords.CountAsync(r => r.CompanyId == scenario.CompanyId)));

        var retry = await ReadAsync(await scenario.SendAsync(keyB, "Reject", "loser reason").WaitAsync(Timeout));
        AssertSafeMismatch(retry, scenario.TaskId, operation.Id, "Reject", "Approve", "winner reason", "loser reason");

        Assert.Equal(1, scenario.Action.Calls);
        Assert.Equal(keyA is null ? 0 : 1,
            await TasksAsync(db => db.IdempotencyRecords.CountAsync(r => r.CompanyId == scenario.CompanyId)));
        AssertOperationUnchanged(claimed, Assert.Single(await ActiveOperationsAsync(scenario)));
    }

    [Theory]
    [InlineData("key-a", "key-b")]
    [InlineData(null, null)]
    public async Task Same_Decision_With_A_Different_Reason_Loses_With_Mismatch(string? keyA, string? keyB)
    {
        using var scenario = await NewScenarioAsync();

        var requestA = scenario.SendAsync(keyA, "Approve", "first reason");
        await scenario.Action.Entered.WaitAsync(Timeout);
        var claimed = Assert.Single(await ActiveOperationsAsync(scenario));

        var loser = await ReadAsync(await scenario.SendAsync(keyB, "Approve", "second reason").WaitAsync(Timeout));

        AssertSafeMismatch(loser, scenario.TaskId, claimed.Id, "first reason", "second reason");

        scenario.Action.Release();
        Assert.Equal(HttpStatusCode.OK, (await ReadAsync(await requestA.WaitAsync(Timeout))).Status);

        var operation = Assert.Single(await ActiveOperationsAsync(scenario));
        Assert.Equal("first reason", operation.OutcomeReason);
        Assert.Equal("Approve", operation.OutcomeDecision);
        Assert.Equal(1, scenario.Action.Calls);
        Assert.Equal("first reason", Assert.Single(scenario.Action.Contexts).OutcomeReason);
        await AssertSingleCompletionEffectsAsync(scenario);
    }

    [Theory]
    [InlineData("same-key", "same-key")]
    [InlineData("key-a", "key-b")]
    [InlineData(null, null)]
    public async Task Command_Equivalent_After_Normalisation_Converges_And_Dispatches_Once(string? keyA, string? keyB)
    {
        using var scenario = await NewScenarioAsync();

        var requestA = scenario.SendAsync(keyA, "Approve", "because");
        await scenario.Action.Entered.WaitAsync(Timeout);

        var pending = await ReadAsync(await scenario.SendAsync(keyB, "  Approve ", "  because\n").WaitAsync(Timeout));

        Assert.Equal(HttpStatusCode.OK, pending.Status);
        Assert.Equal("pending", pending.Json.GetProperty("effectsStatus").GetString());
        Assert.Equal(1, scenario.Action.Calls);

        scenario.Action.Release();
        Assert.Equal(HttpStatusCode.OK, (await ReadAsync(await requestA.WaitAsync(Timeout))).Status);

        var confirmed = await ReadAsync(await scenario.SendAsync(keyB, "Approve", "because").WaitAsync(Timeout));
        Assert.Equal(HttpStatusCode.OK, confirmed.Status);
        Assert.Equal("confirmed", confirmed.Json.GetProperty("effectsStatus").GetString());

        var operation = Assert.Single(await ActiveOperationsAsync(scenario));
        Assert.Equal("Approve", operation.OutcomeDecision);
        Assert.Equal("because", operation.OutcomeReason);
        Assert.Equal(1, scenario.Action.Calls);
        await AssertSingleCompletionEffectsAsync(scenario);
    }

    [Fact]
    public async Task Different_Authorised_User_With_The_Same_Command_Converges_On_The_Original_Actor()
    {
        using var scenario = await NewScenarioAsync();
        var hrAdmin = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(factory, hrAdmin, SystemRoles.Employee, scenario.CompanyId);
        await TestRoleSeeder.AssignRoleAsync(factory, hrAdmin, SystemRoles.HrAdministrator, scenario.CompanyId);

        var requestA = scenario.SendAsync("actor-a", "Approve", "because");
        await scenario.Action.Entered.WaitAsync(Timeout);
        var claimed = Assert.Single(await ActiveOperationsAsync(scenario));

        var other = await ReadAsync(await scenario.SendAsync("actor-b", "Approve", "because", hrAdmin).WaitAsync(Timeout));
        Assert.Equal(HttpStatusCode.OK, other.Status);
        Assert.Equal("pending", other.Json.GetProperty("effectsStatus").GetString());

        scenario.Action.Release();
        Assert.Equal(HttpStatusCode.OK, (await ReadAsync(await requestA.WaitAsync(Timeout))).Status);

        var later = await ReadAsync(await scenario.SendAsync("actor-c", "Approve", "because", hrAdmin).WaitAsync(Timeout));
        Assert.Equal(HttpStatusCode.OK, later.Status);
        Assert.Equal(scenario.EmployeeId, later.Json.GetProperty("completedBy").GetGuid());

        var operation = Assert.Single(await ActiveOperationsAsync(scenario));
        AssertOperationUnchanged(claimed, operation);
        Assert.Equal(scenario.EmployeeId, operation.CompletedBy);
        Assert.Equal(1, scenario.Action.Calls);
        await AssertSingleCompletionEffectsAsync(scenario);
    }

    [Theory]
    [InlineData("later-key")]
    [InlineData(null)]
    public async Task Conflicting_Command_After_The_Task_Completed_Is_Rejected_Without_Mutation(string? laterKey)
    {
        using var scenario = await NewScenarioAsync();
        scenario.Action.Release();

        Assert.Equal(HttpStatusCode.OK, (await ReadAsync(await scenario.SendAsync("first-key", "Approve", "because"))).Status);
        var before = Assert.Single(await ActiveOperationsAsync(scenario));
        var recordsBefore = await TasksAsync(db => db.IdempotencyRecords.CountAsync(r => r.CompanyId == scenario.CompanyId));

        var conflicting = await ReadAsync(await scenario.SendAsync(laterKey, "Reject", "changed my mind"));

        AssertSafeMismatch(conflicting, scenario.TaskId, before.Id, "Reject", "Approve", "because", "changed my mind");

        var after = Assert.Single(await ActiveOperationsAsync(scenario));
        AssertOperationUnchanged(before, after);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(recordsBefore, await TasksAsync(db => db.IdempotencyRecords.CountAsync(r => r.CompanyId == scenario.CompanyId)));
        Assert.Equal(1, scenario.Action.Calls);
        await AssertSingleCompletionEffectsAsync(scenario);

        var matching = await ReadAsync(await scenario.SendAsync(laterKey, "Approve", "because"));
        Assert.Equal(HttpStatusCode.OK, matching.Status);
        Assert.Equal(1, scenario.Action.Calls);
    }
}
