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
using HR.Modules.Tasks.Jobs;
using HR.Modules.Tasks.Persistence;
using HR.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class CompleteTaskIdempotentReplayEndpointTests
{
    private static readonly Guid OperatorUser = new("cc0000e1-0000-0000-0000-000000000001");

    private readonly ApiWebApplicationFactory _factory;

    public CompleteTaskIdempotentReplayEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(() => TestRoleSeeder.AssignRoleAsync(factory, OperatorUser, SystemRoles.CompanyAdministrator))
            .GetAwaiter().GetResult();
    }

    private sealed class CountingWorkflowAction : ITaskCompletionAction
    {
        private int _calls;

        public TaskSource Source => TaskSource.Workflow;
        public TaskActionType ActionType => TaskActionType.Complete;
        public int Calls => _calls;

        public Task<Result> ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class Scenario(
        CompleteTaskIdempotentReplayEndpointTests owner,
        WebApplicationFactory<Program> factory,
        CountingWorkflowAction action,
        Guid companyId,
        Guid employeeId,
        Guid taskId,
        HttpClient client)
        : IDisposable
    {
        public Guid CompanyId => companyId;
        public Guid EmployeeId => employeeId;
        public Guid TaskId => taskId;
        public int DispatchCalls => action.Calls;
        public HttpClient Client => client;

        public string Url => $"/api/companies/{companyId}/tasks/{taskId}/complete";

        public async Task<HttpResponseMessage> CompleteAsync(string key = "replay-key")
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Url) { Content = JsonContent.Create(new { }) };
            request.Headers.Add("Idempotency-Key", key);
            return await client.SendAsync(request);
        }

        public async Task<HttpClient> OperatorClientAsync() =>
            await owner.OperatorClientAsync(factory, companyId);

        public void Dispose()
        {
            client.Dispose();
        }
    }

    private async Task<Scenario> NewScenarioAsync()
    {
        var action = new CountingWorkflowAction();
        var factory = RoutedCompletionActionHost.For(_factory).Factory;

        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var taskId = await TaskSeeder.SeedAsync(
            _factory, companyId, "Record feedback", "private description", assignedEmployeeId: employeeId);

        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee, companyId);

        RoutedCompletionActionHost.For(_factory).Register(taskId, action);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, employeeId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        return new Scenario(this, factory, action, companyId, employeeId, taskId, client);
    }

    private async Task<HttpClient> OperatorClientAsync(WebApplicationFactory<Program> factory, Guid companyId)
    {
        await TestRoleSeeder.AssignRoleAsync(_factory, OperatorUser, SystemRoles.CompanyAdministrator, companyId);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, OperatorUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private async Task<T> TasksAsync<T>(Func<TasksDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<TasksDbContext>());
    }

    private Task<TaskCompletionOperation> OperationAsync(Guid taskId) =>
        TasksAsync(db => db.TaskCompletionOperations.AsNoTracking().SingleAsync(o => o.TaskId == taskId));

    private async Task MutateAsync(Guid taskId, Action<TaskCompletionOperation> mutate)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        var operation = await db.TaskCompletionOperations.SingleAsync(o => o.TaskId == taskId);
        mutate(operation);
        await db.SaveChangesAsync();
    }

    private async Task<int> IdempotencyRecordCountAsync(Guid companyId) =>
        await TasksAsync(db => db.IdempotencyRecords.CountAsync(r => r.CompanyId == companyId));

    private async Task AssertSingleEffectsAsync(Scenario scenario)
    {
        Assert.Equal(1, scenario.DispatchCalls);

        using var scope = _factory.Services.CreateScope();
        var notifications = await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>()
            .Notifications.CountAsync(n => n.SourceEntityId == scenario.TaskId && n.Type == NotificationType.TaskCompleted);
        var audits = await scope.ServiceProvider.GetRequiredService<AuditDbContext>()
            .AuditEvents.CountAsync(e => e.EventId == scenario.TaskId);
        Assert.Equal(1, notifications);
        Assert.Equal(1, audits);
        Assert.Equal(1, await IdempotencyRecordCountAsync(scenario.CompanyId));
    }

    private static async Task<(HttpStatusCode Status, string? Effects, string? Resolution, string? Code, JsonElement Root)> ReadAsync(
        HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        var root = JsonDocument.Parse(raw).RootElement.Clone();
        string? Prop(string name) =>
            root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString()
                : null;
        return (response.StatusCode, Prop("effectsStatus"), Prop("resolutionType"), Prop("code"), root);
    }

    [Fact]
    public async Task Same_Key_Replay_Follows_The_Live_Operation_State_Without_Repeating_Any_Mutation()
    {
        using var scenario = await NewScenarioAsync();

        var original = await ReadAsync(await scenario.CompleteAsync());
        Assert.Equal(HttpStatusCode.OK, original.Status);
        Assert.Equal("confirmed", original.Effects);
        Assert.Equal(TaskCompletionOperation.StatusProcessed, (await OperationAsync(scenario.TaskId)).Status);

        var replayProcessed = await ReadAsync(await scenario.CompleteAsync());
        Assert.Equal(HttpStatusCode.OK, replayProcessed.Status);
        Assert.Equal("confirmed", replayProcessed.Effects);

        await MutateAsync(scenario.TaskId, o => o.ResetEffectsTerminalFailure(OperatorUser, DateTimeOffset.UtcNow));
        var replayPending = await ReadAsync(await scenario.CompleteAsync());
        Assert.Equal(HttpStatusCode.OK, replayPending.Status);
        Assert.Equal("pending", replayPending.Effects);

        await MutateAsync(scenario.TaskId, o => o.MarkEffectsTerminalFailure(
            TaskCompletionOperation.CategoryEffectsRetryLimit, "secret exception text", DateTimeOffset.UtcNow));
        var terminalResponse = await scenario.CompleteAsync();
        var terminalRaw = await terminalResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Conflict, terminalResponse.StatusCode);
        using (var json = JsonDocument.Parse(terminalRaw))
        {
            Assert.Equal("conflict.effects_terminal_failure", json.RootElement.GetProperty("code").GetString());
            var details = json.RootElement.GetProperty("details");
            Assert.Equal(scenario.TaskId, details.GetProperty("taskId").GetGuid());
            Assert.True(details.GetProperty("resettable").GetBoolean());
            Assert.Equal("reset", details.GetProperty("recoveryAction").GetString());
        }
        Assert.DoesNotContain("secret exception text", terminalRaw);
        Assert.DoesNotContain("private description", terminalRaw);

        var operationId = (await OperationAsync(scenario.TaskId)).Id;
        using (var operatorClient = await scenario.OperatorClientAsync())
        {
            var reset = await operatorClient.PostAsJsonAsync(
                $"/api/companies/{scenario.CompanyId}/tasks/completion-operations/{operationId}/reset",
                new { reason = "retry" });
            Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        }

        var replayAfterReset = await ReadAsync(await scenario.CompleteAsync());
        Assert.Equal(HttpStatusCode.OK, replayAfterReset.Status);
        Assert.Equal("pending", replayAfterReset.Effects);

        using (var reprocessScope = _factory.Services.CreateScope())
        {
            await reprocessScope.ServiceProvider.GetRequiredService<TaskCompletionEffectsJob>()
                .ProcessAsync(operationId, scenario.CompanyId);
        }

        var replayReprocessed = await ReadAsync(await scenario.CompleteAsync());
        Assert.Equal(HttpStatusCode.OK, replayReprocessed.Status);
        Assert.Equal("confirmed", replayReprocessed.Effects);

        await AssertSingleEffectsAsync(scenario);
    }

    [Fact]
    public async Task Same_Key_Replay_Reports_A_Data_Integrity_Failure_As_The_Typed_Adjudication_Conflict()
    {
        using var scenario = await NewScenarioAsync();
        Assert.Equal(HttpStatusCode.OK, (await scenario.CompleteAsync()).StatusCode);
        await MutateAsync(scenario.TaskId, o => o.MarkDataIntegrityFailure("secret exception text", DateTimeOffset.UtcNow));

        var replay = await ReadAsync(await scenario.CompleteAsync());

        Assert.Equal(HttpStatusCode.Conflict, replay.Status);
        Assert.Equal("conflict.data_integrity_failure", replay.Code);
        Assert.Equal("adjudicate", replay.Root.GetProperty("details").GetProperty("recoveryAction").GetString());
        await AssertSingleEffectsAsync(scenario);
    }

    [Fact]
    public async Task Same_Key_Replay_Reports_Verified_And_Waived_Resolutions_Distinctly_From_Confirmed()
    {
        using var verified = await NewScenarioAsync();
        using var waived = await NewScenarioAsync();

        foreach (var scenario in new[] { verified, waived })
        {
            Assert.Equal(HttpStatusCode.OK, (await scenario.CompleteAsync()).StatusCode);
            await MutateAsync(scenario.TaskId, o => o.MarkDataIntegrityFailure("x", DateTimeOffset.UtcNow));
        }

        var verifiedOperation = await OperationAsync(verified.TaskId);
        var waivedOperation = await OperationAsync(waived.TaskId);

        using (var verifiedOperator = await verified.OperatorClientAsync())
        {
            var response = await verifiedOperator.PostAsJsonAsync(
                $"/api/companies/{verified.CompanyId}/tasks/completion-operations/{verifiedOperation.Id}/adjudicate",
                new
                {
                    resolution = "effects_verified",
                    reason = "Checked",
                    evidence = new
                    {
                        notificationRequired = true,
                        assignedEmployeeId = verified.EmployeeId,
                        completedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                        previousTaskStatus = "Open",
                        taskTitle = "Record feedback",
                    },
                });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using (var waivedOperator = await waived.OperatorClientAsync())
        {
            var response = await waivedOperator.PostAsJsonAsync(
                $"/api/companies/{waived.CompanyId}/tasks/completion-operations/{waivedOperation.Id}/adjudicate",
                new { resolution = "waived", reason = "Unrecoverable" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var verifiedReplay = await ReadAsync(await verified.CompleteAsync());
        var waivedReplay = await ReadAsync(await waived.CompleteAsync());

        Assert.Equal(HttpStatusCode.OK, verifiedReplay.Status);
        Assert.Equal("verified", verifiedReplay.Effects);
        Assert.Equal("effects_verified", verifiedReplay.Resolution);
        Assert.Equal(HttpStatusCode.OK, waivedReplay.Status);
        Assert.Equal("waived", waivedReplay.Effects);
        Assert.Equal("waived", waivedReplay.Resolution);
        await AssertSingleEffectsAsync(verified);
        await AssertSingleEffectsAsync(waived);
    }

    [Fact]
    public async Task Same_Key_Replay_Requires_Authentication_Authorization_And_The_Same_Company()
    {
        using var scenario = await NewScenarioAsync();
        Assert.Equal(HttpStatusCode.OK, (await scenario.CompleteAsync()).StatusCode);

        using var anonymous = _factory.CreateClient();
        using var anonymousRequest = new HttpRequestMessage(HttpMethod.Post, scenario.Url) { Content = JsonContent.Create(new { }) };
        anonymousRequest.Headers.Add("Idempotency-Key", "replay-key");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(anonymousRequest)).StatusCode);

        var otherEmployee = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, otherEmployee, SystemRoles.Employee, scenario.CompanyId);
        using var stranger = _factory.CreateClient();
        stranger.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, otherEmployee.ToString());
        stranger.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, scenario.CompanyId.ToString());
        using var strangerRequest = new HttpRequestMessage(HttpMethod.Post, scenario.Url) { Content = JsonContent.Create(new { }) };
        strangerRequest.Headers.Add("Idempotency-Key", "replay-key");
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.SendAsync(strangerRequest)).StatusCode);

        var otherCompany = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, scenario.EmployeeId, SystemRoles.Employee, otherCompany);
        using var foreign = _factory.CreateClient();
        foreign.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, scenario.EmployeeId.ToString());
        foreign.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, otherCompany.ToString());
        using var foreignRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/companies/{otherCompany}/tasks/{scenario.TaskId}/complete") { Content = JsonContent.Create(new { }) };
        foreignRequest.Headers.Add("Idempotency-Key", "replay-key");
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.SendAsync(foreignRequest)).StatusCode);

        await AssertSingleEffectsAsync(scenario);
    }

    [Fact]
    public async Task Racing_A_Terminal_Transition_With_Replays_Reports_A_Consistent_State_And_Repeats_Nothing()
    {
        using var scenario = await NewScenarioAsync();
        Assert.Equal(HttpStatusCode.OK, (await scenario.CompleteAsync()).StatusCode);

        var replays = Task.Run(async () =>
        {
            var seen = new List<(HttpStatusCode Status, string? Effects, string? Code)>();
            for (var i = 0; i < 40; i++)
            {
                var read = await ReadAsync(await scenario.CompleteAsync());
                seen.Add((read.Status, read.Effects, read.Code));
            }
            return seen;
        });

        var transition = Task.Run(async () =>
        {
            await Task.Delay(30);
            await MutateAsync(scenario.TaskId, o => o.MarkEffectsTerminalFailure(
                TaskCompletionOperation.CategoryEffectsRetryLimit, "x", DateTimeOffset.UtcNow));
        });

        await Task.WhenAll(replays, transition);
        var observed = await replays;

        Assert.All(observed, r =>
        {
            if (r.Status == HttpStatusCode.OK)
                Assert.Contains(r.Effects, new[] { "confirmed", "pending" });
            else
            {
                Assert.Equal(HttpStatusCode.Conflict, r.Status);
                Assert.Equal("conflict.effects_terminal_failure", r.Code);
            }
        });

        var firstConflict = observed.FindIndex(r => r.Status == HttpStatusCode.Conflict);
        if (firstConflict >= 0)
            Assert.All(observed.Skip(firstConflict), r => Assert.Equal(HttpStatusCode.Conflict, r.Status));

        var final = await ReadAsync(await scenario.CompleteAsync());
        Assert.Equal(HttpStatusCode.Conflict, final.Status);
        await AssertSingleEffectsAsync(scenario);
    }

    [Fact]
    public async Task Different_Request_With_An_Existing_Key_Is_Still_A_Key_Reuse_Conflict()
    {
        using var scenario = await NewScenarioAsync();
        Assert.Equal(HttpStatusCode.OK, (await scenario.CompleteAsync()).StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Post, scenario.Url)
        {
            Content = JsonContent.Create(new { outcomeDecision = "approve" }),
        };
        request.Headers.Add("Idempotency-Key", "replay-key");
        var response = await scenario.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertSingleEffectsAsync(scenario);
    }
}
