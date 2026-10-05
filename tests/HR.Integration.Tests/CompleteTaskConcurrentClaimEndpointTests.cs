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
public class CompleteTaskConcurrentClaimEndpointTests(ApiWebApplicationFactory factory)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private sealed class BlockingWorkflowAction : ITaskCompletionAction
    {
        private int _calls;
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<Guid> _operationIds = [];

        public TaskSource Source => TaskSource.Workflow;
        public TaskActionType ActionType => TaskActionType.Complete;
        public int Calls => _calls;
        public Task Entered => _entered.Task;
        public IReadOnlyList<Guid> OperationIds { get { lock (_operationIds) return _operationIds.ToList(); } }

        public void Release() => _release.TrySetResult();

        public async Task<Result> ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            lock (_operationIds)
                _operationIds.Add(context.DispatchOperationId);
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
        public Guid TaskId => taskId;
        public BlockingWorkflowAction Action => action;
        public string Url => $"/api/companies/{companyId}/tasks/{taskId}/complete";

        public HttpClient NewClient()
        {
            var client = appFactory.CreateClient();
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, employeeId.ToString());
            client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
            return client;
        }

        public Task<HttpResponseMessage> SendAsync(string? key)
        {
            var client = NewClient();
            var request = new HttpRequestMessage(HttpMethod.Post, Url) { Content = JsonContent.Create(new { }) };
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
            factory, companyId, "Record feedback", "private description", assignedEmployeeId: employeeId);
        await TestRoleSeeder.AssignRoleAsync(factory, employeeId, SystemRoles.Employee, companyId);

        return new Scenario(appFactory, action, companyId, employeeId, taskId);
    }

    private async Task<T> TasksAsync<T>(Func<TasksDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<TasksDbContext>());
    }

    private static async Task<(HttpStatusCode Status, string? Effects, string Raw)> ReadAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        string? effects = null;
        using var json = JsonDocument.Parse(raw);
        if (json.RootElement.ValueKind == JsonValueKind.Object
            && json.RootElement.TryGetProperty("effectsStatus", out var property)
            && property.ValueKind == JsonValueKind.String)
            effects = property.GetString();
        return (response.StatusCode, effects, raw);
    }

    private async Task RunRaceAsync(string? keyA, string? keyB, int expectedIdempotencyRecords)
    {
        using var scenario = await NewScenarioAsync();

        var requestA = scenario.SendAsync(keyA);
        await scenario.Action.Entered.WaitAsync(Timeout);

        var loser = await ReadAsync(await scenario.SendAsync(keyB).WaitAsync(Timeout));

        Assert.Equal(HttpStatusCode.OK, loser.Status);
        Assert.Equal("pending", loser.Effects);
        Assert.Equal(1, scenario.Action.Calls);

        scenario.Action.Release();
        var winner = await ReadAsync(await requestA.WaitAsync(Timeout));

        Assert.Equal(HttpStatusCode.OK, winner.Status);
        Assert.Equal("confirmed", winner.Effects);
        Assert.Equal(1, scenario.Action.Calls);

        var operations = await TasksAsync(db => db.TaskCompletionOperations.AsNoTracking()
            .Where(o => o.TaskId == scenario.TaskId && o.Status != TaskCompletionOperation.StatusRejected)
            .ToListAsync());
        var operation = Assert.Single(operations);
        Assert.Equal(TaskCompletionOperation.StatusProcessed, operation.Status);
        Assert.Null(operation.ClaimedBy);
        Assert.Equal(operation.Id, Assert.Single(scenario.Action.OperationIds));

        var task = await TasksAsync(db => db.TaskItems.AsNoTracking().SingleAsync(t => t.Id == scenario.TaskId));
        Assert.Equal(TaskItemStatus.Completed, task.Status);
        Assert.Equal(expectedIdempotencyRecords,
            await TasksAsync(db => db.IdempotencyRecords.CountAsync(r => r.CompanyId == scenario.CompanyId)));

        using (var scope = factory.Services.CreateScope())
        {
            var notifications = await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>()
                .Notifications.CountAsync(n => n.SourceEntityId == scenario.TaskId && n.Type == NotificationType.TaskCompleted);
            var audits = await scope.ServiceProvider.GetRequiredService<AuditDbContext>()
                .AuditEvents.CountAsync(e => e.EventId == scenario.TaskId);
            Assert.Equal(1, notifications);
            Assert.Equal(1, audits);
        }

        var later = await ReadAsync(await scenario.SendAsync(keyA).WaitAsync(Timeout));
        Assert.Equal(HttpStatusCode.OK, later.Status);
        Assert.Equal("confirmed", later.Effects);
        Assert.Equal(1, scenario.Action.Calls);
    }

    [Fact]
    public Task Concurrent_Requests_With_The_Same_Key_Dispatch_Exactly_Once() =>
        RunRaceAsync("race-key", "race-key", expectedIdempotencyRecords: 1);

    [Fact]
    public Task Concurrent_Requests_With_Different_Keys_Dispatch_Exactly_Once() =>
        RunRaceAsync("race-key-a", "race-key-b", expectedIdempotencyRecords: 1);

    [Fact]
    public Task Concurrent_Requests_Without_Keys_Dispatch_Exactly_Once() =>
        RunRaceAsync(null, null, expectedIdempotencyRecords: 0);

    [Fact]
    public async Task Live_Claim_Persisted_By_Another_Worker_Blocks_Http_Dispatch_And_Reports_Pending()
    {
        using var scenario = await NewScenarioAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
            var operation = TaskCompletionOperation.CreatePending(
                Guid.NewGuid(), scenario.CompanyId, scenario.TaskId, Guid.NewGuid(), null, null, DateTimeOffset.UtcNow);
            operation.Claim(Guid.NewGuid(), DateTimeOffset.UtcNow);
            db.TaskCompletionOperations.Add(operation);
            await db.SaveChangesAsync();
        }

        var response = await ReadAsync(await scenario.SendAsync("claimed-key").WaitAsync(Timeout));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("pending", response.Effects);
        Assert.Equal(0, scenario.Action.Calls);
        Assert.Equal(0, await TasksAsync(db => db.IdempotencyRecords.CountAsync(r => r.CompanyId == scenario.CompanyId)));
        Assert.Equal(TaskItemStatus.Open,
            (await TasksAsync(db => db.TaskItems.AsNoTracking().SingleAsync(t => t.Id == scenario.TaskId))).Status);
    }
}
