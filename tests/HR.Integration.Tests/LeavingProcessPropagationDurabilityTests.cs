using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Jobs;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Leave.Features.RecalculateEntitlementOnLeavingDateChange;
using HR.Modules.Leave.Persistence;
using HR.Modules.Offboarding.Features.CancelOffboardingOnLeavingProcessCancelled;
using HR.Modules.Offboarding.Features.RescheduleOffboardingOnLeavingDateChanged;
using HR.Modules.Offboarding.Persistence;
using HR.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HR.Integration.Tests;

internal sealed class ConsumerFailureSwitch
{
    public volatile bool FailLeave;
    public volatile bool FailOffboarding;
}

internal sealed class SwitchableLeaveConsumer(LeavingDateChangeHandler inner, ConsumerFailureSwitch failureSwitch)
    : IRequiredIntegrationEventHandler<EmployeeLeavingDateSetIntegrationEvent>,
      IRequiredIntegrationEventHandler<EmployeeLeavingProcessCancelledIntegrationEvent>
{
    public Task HandleAsync(EmployeeLeavingDateSetIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        failureSwitch.FailLeave
            ? throw new InvalidOperationException("Simulated Leave consumer failure.")
            : inner.HandleAsync(integrationEvent, cancellationToken);

    public Task HandleAsync(EmployeeLeavingProcessCancelledIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        failureSwitch.FailLeave
            ? throw new InvalidOperationException("Simulated Leave consumer failure.")
            : inner.HandleAsync(integrationEvent, cancellationToken);
}

internal sealed class SwitchableOffboardingRescheduleConsumer(
    RescheduleOffboardingOnLeavingDateChangedHandler inner, ConsumerFailureSwitch failureSwitch)
    : IRequiredIntegrationEventHandler<EmployeeLeavingDateSetIntegrationEvent>
{
    public Task HandleAsync(EmployeeLeavingDateSetIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        failureSwitch.FailOffboarding
            ? throw new InvalidOperationException("Simulated Offboarding reschedule failure.")
            : inner.HandleAsync(integrationEvent, cancellationToken);
}

internal sealed class SwitchableOffboardingCancelConsumer(
    CancelOffboardingOnLeavingProcessCancelledHandler inner, ConsumerFailureSwitch failureSwitch)
    : IRequiredIntegrationEventHandler<EmployeeLeavingProcessCancelledIntegrationEvent>
{
    public Task HandleAsync(EmployeeLeavingProcessCancelledIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        failureSwitch.FailOffboarding
            ? throw new InvalidOperationException("Simulated Offboarding cancel failure.")
            : inner.HandleAsync(integrationEvent, cancellationToken);
}

[Collection("Integration")]
public class LeavingProcessPropagationDurabilityTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = new("ffffffff-5100-0000-0000-000000000001");

    public LeavingProcessPropagationDurabilityTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private WebApplicationFactory<Program> CreateFlakyFactory(ConsumerFailureSwitch failureSwitch) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(failureSwitch);
                services.AddScoped<LeavingDateChangeHandler>();
                services.AddScoped<RescheduleOffboardingOnLeavingDateChangedHandler>();
                services.AddScoped<CancelOffboardingOnLeavingProcessCancelledHandler>();

                services.RemoveAll<IIntegrationEventHandler<EmployeeLeavingDateSetIntegrationEvent>>();
                services.RemoveAll<IIntegrationEventHandler<EmployeeLeavingProcessCancelledIntegrationEvent>>();

                services.AddScoped<IIntegrationEventHandler<EmployeeLeavingDateSetIntegrationEvent>, SwitchableLeaveConsumer>();
                services.AddScoped<IIntegrationEventHandler<EmployeeLeavingProcessCancelledIntegrationEvent>, SwitchableLeaveConsumer>();
                services.AddScoped<IIntegrationEventHandler<EmployeeLeavingDateSetIntegrationEvent>, SwitchableOffboardingRescheduleConsumer>();
                services.AddScoped<IIntegrationEventHandler<EmployeeLeavingProcessCancelledIntegrationEvent>, SwitchableOffboardingCancelConsumer>();
            });
        });

    private static HttpClient ClientFor(WebApplicationFactory<Program> factory, Guid companyId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private async Task<HttpClient> SeedClientAsync(Guid companyId)
    {
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUser, SystemRoles.HrAdministrator, companyId);
        return ClientFor(_factory, companyId);
    }

    private async Task<Guid> SeedEmployeeWithLeaveAsync(HttpClient client, Guid companyId)
    {
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<HR.Infrastructure.Abstractions.ILeaveTypeDefaultsProvisioner>()
                .EnsureDefaultLeaveTypesAsync(companyId, default);
        }

        var startDate = new DateOnly(2020, 1, 1);
        var employeeResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Durable", "Leaver", $"durable.{Guid.NewGuid():N}@example.com", startDate: startDate));
        employeeResponse.EnsureSuccessStatusCode();
        var employeeId = (await employeeResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var policyResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"Policy {Guid.NewGuid():N}", carryOverDays = 5, allowNegativeBalance = false });
        policyResponse.EnsureSuccessStatusCode();
        var policyId = (await policyResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var assignResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-policy",
            new { companyId, employeeId, leavePolicyId = policyId, effectiveFrom = startDate.ToString("yyyy-MM-dd") });
        assignResponse.EnsureSuccessStatusCode();

        return employeeId;
    }

    private static DateOnly NearLeavingDate => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);

    private static Task<HttpResponseMessage> StartAsync(
        HttpClient client, Guid companyId, Guid employeeId, DateOnly leavingDate, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/companies/{companyId}/employees/{employeeId}/leaving-process")
        {
            Content = JsonContent.Create(new
            {
                companyId,
                employeeId,
                resignationReceivedDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
                leavingDate = leavingDate.ToString("yyyy-MM-dd"),
                lastWorkingDay = leavingDate.AddDays(-1).ToString("yyyy-MM-dd"),
                leavingReason = "Resignation",
            }),
        };
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> AmendAsync(
        HttpClient client, Guid companyId, Guid employeeId, DateOnly leavingDate, int expectedVersion) =>
        client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                leavingDate = leavingDate.ToString("yyyy-MM-dd"),
                lastWorkingDay = leavingDate.AddDays(-1).ToString("yyyy-MM-dd"),
                leavingReason = "Resignation",
                expectedVersion,
            });

    private static Task<HttpResponseMessage> CancelAsync(
        HttpClient client, Guid companyId, Guid employeeId, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/companies/{companyId}/employees/{employeeId}/leaving-process/cancel")
        {
            Content = JsonContent.Create(new { companyId, employeeId, cancellationReason = "Employee retracted resignation." }),
        };
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        return client.SendAsync(request);
    }

    private async Task<decimal> GetAnnualEntitlementAsync(Guid companyId, Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var type = await db.LeaveTypes.SingleAsync(t => t.CompanyId == companyId && t.Code == "ANNUAL");
        var balance = await db.LeaveBalances.SingleAsync(b =>
            b.CompanyId == companyId && b.EmployeeId == employeeId && b.LeaveTypeId == type.Id
            && b.PolicyYear == DateTime.UtcNow.Year);
        return balance.EntitlementDays;
    }

    private async Task<List<LeavingProcessPropagation>> GetPropagationsAsync(Guid companyId, Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        return await db.LeavingProcessPropagations.AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.EmployeeId == employeeId)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync();
    }

    private async Task<List<(DateOnly? DueDate, string Status)>> GetOffboardingTasksAsync(Guid companyId, Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OffboardingDbContext>();
        var planIds = await db.OffboardingPlans
            .Where(p => p.CompanyId == companyId && p.EmployeeId == employeeId)
            .Select(p => p.Id)
            .ToListAsync();
        var tasks = await db.OffboardingTasks.Where(t => planIds.Contains(t.OffboardingPlanId)).ToListAsync();
        return tasks.Select(t => (t.DueDate, t.Status.ToString())).ToList();
    }

    private async Task MakeDueAsync(Guid companyId, Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        await db.LeavingProcessPropagations
            .Where(p => p.CompanyId == companyId && p.EmployeeId == employeeId && p.Status == LeavingProcessPropagation.StatusPending)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.NextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
    }

    private static async Task RunDispatchJobAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DispatchLeavingProcessPropagationsJob>().ExecuteAsync();
    }

    [Fact]
    public async Task Start_WhenLeaveConsumerFails_StaysPendingWithMetadata_ThenDispatcherRetryAppliesProration()
    {
        var companyId = Guid.NewGuid();
        using var seedClient = await SeedClientAsync(companyId);
        var employeeId = await SeedEmployeeWithLeaveAsync(seedClient, companyId);
        var fullEntitlement = await GetAnnualEntitlementAsync(companyId, employeeId);

        var failureSwitch = new ConsumerFailureSwitch { FailLeave = true };
        await using var flaky = CreateFlakyFactory(failureSwitch);
        using var flakyClient = ClientFor(flaky, companyId);

        var start = await StartAsync(flakyClient, companyId, employeeId, NearLeavingDate);
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);

        var afterFailure = Assert.Single(await GetPropagationsAsync(companyId, employeeId));
        Assert.Equal(LeavingProcessPropagation.StatusPending, afterFailure.Status);
        Assert.Equal(LeavingProcessPropagation.OperationStarted, afterFailure.OperationType);
        Assert.Equal(1, afterFailure.AttemptCount);
        Assert.NotNull(afterFailure.LastError);
        Assert.NotNull(afterFailure.NextAttemptAt);
        Assert.Equal(fullEntitlement, await GetAnnualEntitlementAsync(companyId, employeeId));

        failureSwitch.FailLeave = false;
        await MakeDueAsync(companyId, employeeId);
        await RunDispatchJobAsync(flaky);

        var afterRetry = Assert.Single(await GetPropagationsAsync(companyId, employeeId));
        Assert.Equal(LeavingProcessPropagation.StatusProcessed, afterRetry.Status);
        Assert.Equal(2, afterRetry.AttemptCount);
        Assert.Equal(afterFailure.Id, afterRetry.Id);
        Assert.Equal(afterFailure.CorrelationId, afterRetry.CorrelationId);
        Assert.Equal(afterFailure.CausationId, afterRetry.CausationId);
        Assert.True(await GetAnnualEntitlementAsync(companyId, employeeId) < fullEntitlement);
    }

    [Fact]
    public async Task Cancel_WhenLeaveConsumerFails_ThenDispatcherRetryRestoresEntitlement()
    {
        var companyId = Guid.NewGuid();
        using var seedClient = await SeedClientAsync(companyId);
        var employeeId = await SeedEmployeeWithLeaveAsync(seedClient, companyId);
        var fullEntitlement = await GetAnnualEntitlementAsync(companyId, employeeId);

        var start = await StartAsync(seedClient, companyId, employeeId, NearLeavingDate);
        start.EnsureSuccessStatusCode();
        var reduced = await GetAnnualEntitlementAsync(companyId, employeeId);
        Assert.True(reduced < fullEntitlement);

        var failureSwitch = new ConsumerFailureSwitch { FailLeave = true };
        await using var flaky = CreateFlakyFactory(failureSwitch);
        using var flakyClient = ClientFor(flaky, companyId);

        var cancel = await CancelAsync(flakyClient, companyId, employeeId);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        var cancelled = (await GetPropagationsAsync(companyId, employeeId))
            .Single(p => p.OperationType == LeavingProcessPropagation.OperationCancelled);
        Assert.Equal(LeavingProcessPropagation.StatusPending, cancelled.Status);
        Assert.Equal(reduced, await GetAnnualEntitlementAsync(companyId, employeeId));

        failureSwitch.FailLeave = false;
        await MakeDueAsync(companyId, employeeId);
        await RunDispatchJobAsync(flaky);

        var afterRetry = (await GetPropagationsAsync(companyId, employeeId))
            .Single(p => p.OperationType == LeavingProcessPropagation.OperationCancelled);
        Assert.Equal(LeavingProcessPropagation.StatusProcessed, afterRetry.Status);
        Assert.Equal(fullEntitlement, await GetAnnualEntitlementAsync(companyId, employeeId));
    }

    [Fact]
    public async Task Amend_WhenOffboardingConsumerFails_ThenDispatcherRetryReschedulesOutstandingTasks()
    {
        var companyId = Guid.NewGuid();
        using var seedClient = await SeedClientAsync(companyId);
        var employeeId = await SeedEmployeeWithLeaveAsync(seedClient, companyId);

        var originalLeavingDate = NearLeavingDate;
        (await StartAsync(seedClient, companyId, employeeId, originalLeavingDate)).EnsureSuccessStatusCode();

        var tasksBefore = await GetOffboardingTasksAsync(companyId, employeeId);
        Assert.NotEmpty(tasksBefore);
        Assert.All(tasksBefore, t => Assert.Equal(originalLeavingDate.AddDays(-1), t.DueDate));

        var failureSwitch = new ConsumerFailureSwitch { FailOffboarding = true };
        await using var flaky = CreateFlakyFactory(failureSwitch);
        using var flakyClient = ClientFor(flaky, companyId);

        var newLeavingDate = originalLeavingDate.AddDays(20);
        var amend = await AmendAsync(flakyClient, companyId, employeeId, newLeavingDate, expectedVersion: 1);
        Assert.Equal(HttpStatusCode.OK, amend.StatusCode);

        var amended = (await GetPropagationsAsync(companyId, employeeId))
            .Single(p => p.OperationType == LeavingProcessPropagation.OperationAmended);
        Assert.Equal(LeavingProcessPropagation.StatusPending, amended.Status);
        Assert.Equal(newLeavingDate.AddDays(-1), amended.LastWorkingDay);
        Assert.All(await GetOffboardingTasksAsync(companyId, employeeId),
            t => Assert.Equal(originalLeavingDate.AddDays(-1), t.DueDate));

        failureSwitch.FailOffboarding = false;
        await MakeDueAsync(companyId, employeeId);
        await RunDispatchJobAsync(flaky);

        Assert.All(await GetOffboardingTasksAsync(companyId, employeeId),
            t => Assert.Equal(newLeavingDate.AddDays(-1), t.DueDate));
        Assert.All(await GetPropagationsAsync(companyId, employeeId),
            p => Assert.Equal(LeavingProcessPropagation.StatusProcessed, p.Status));
    }

    [Fact]
    public async Task PendingMessage_SurvivesHostRestart_AndIsAppliedByTheNewHost()
    {
        var companyId = Guid.NewGuid();
        using var seedClient = await SeedClientAsync(companyId);
        var employeeId = await SeedEmployeeWithLeaveAsync(seedClient, companyId);
        var fullEntitlement = await GetAnnualEntitlementAsync(companyId, employeeId);

        var failureSwitch = new ConsumerFailureSwitch { FailLeave = true, FailOffboarding = true };
        await using (var firstHost = CreateFlakyFactory(failureSwitch))
        {
            using var client = ClientFor(firstHost, companyId);
            (await StartAsync(client, companyId, employeeId, NearLeavingDate)).EnsureSuccessStatusCode();
        }

        var persisted = Assert.Single(await GetPropagationsAsync(companyId, employeeId));
        Assert.Equal(LeavingProcessPropagation.StatusPending, persisted.Status);
        Assert.Equal(fullEntitlement, await GetAnnualEntitlementAsync(companyId, employeeId));

        await MakeDueAsync(companyId, employeeId);

        await using var secondHost = CreateFlakyFactory(new ConsumerFailureSwitch());
        await RunDispatchJobAsync(secondHost);

        var applied = Assert.Single(await GetPropagationsAsync(companyId, employeeId));
        Assert.Equal(LeavingProcessPropagation.StatusProcessed, applied.Status);
        Assert.Equal(persisted.Id, applied.Id);
        Assert.Equal(persisted.CorrelationId, applied.CorrelationId);
        Assert.Equal(persisted.CausationId, applied.CausationId);
        Assert.True(await GetAnnualEntitlementAsync(companyId, employeeId) < fullEntitlement);
    }

    [Fact]
    public async Task Start_ReplayWithSameIdempotencyKey_RepairsMissingDownstreamEffect_WithoutDuplicates()
    {
        var companyId = Guid.NewGuid();
        using var seedClient = await SeedClientAsync(companyId);
        var employeeId = await SeedEmployeeWithLeaveAsync(seedClient, companyId);
        var fullEntitlement = await GetAnnualEntitlementAsync(companyId, employeeId);
        var leavingDate = NearLeavingDate;
        var key = $"start-{Guid.NewGuid():N}";

        var failureSwitch = new ConsumerFailureSwitch { FailLeave = true };
        await using var flaky = CreateFlakyFactory(failureSwitch);
        using var flakyClient = ClientFor(flaky, companyId);

        var first = await StartAsync(flakyClient, companyId, employeeId, leavingDate, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<IdPayload>();
        Assert.Equal(fullEntitlement, await GetAnnualEntitlementAsync(companyId, employeeId));

        failureSwitch.FailLeave = false;
        await MakeDueAsync(companyId, employeeId);

        var replay = await StartAsync(flakyClient, companyId, employeeId, leavingDate, key);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(firstBody!.Id, (await replay.Content.ReadFromJsonAsync<IdPayload>())!.Id);

        Assert.True(await GetAnnualEntitlementAsync(companyId, employeeId) < fullEntitlement);
        var rows = await GetPropagationsAsync(companyId, employeeId);
        Assert.Single(rows);
        Assert.Equal(LeavingProcessPropagation.StatusProcessed, rows[0].Status);

        using var scope = _factory.Services.CreateScope();
        var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        Assert.Equal(1, await employeesDb.EmployeeLeavingProcesses.CountAsync(p => p.CompanyId == companyId && p.EmployeeId == employeeId));
    }

    [Fact]
    public async Task Cancel_ReplayWithSameIdempotencyKey_RepairsMissingDownstreamEffect()
    {
        var companyId = Guid.NewGuid();
        using var seedClient = await SeedClientAsync(companyId);
        var employeeId = await SeedEmployeeWithLeaveAsync(seedClient, companyId);
        var fullEntitlement = await GetAnnualEntitlementAsync(companyId, employeeId);
        (await StartAsync(seedClient, companyId, employeeId, NearLeavingDate)).EnsureSuccessStatusCode();
        var key = $"cancel-{Guid.NewGuid():N}";

        var failureSwitch = new ConsumerFailureSwitch { FailLeave = true };
        await using var flaky = CreateFlakyFactory(failureSwitch);
        using var flakyClient = ClientFor(flaky, companyId);

        Assert.Equal(HttpStatusCode.OK, (await CancelAsync(flakyClient, companyId, employeeId, key)).StatusCode);
        Assert.True(await GetAnnualEntitlementAsync(companyId, employeeId) < fullEntitlement);

        failureSwitch.FailLeave = false;
        await MakeDueAsync(companyId, employeeId);

        Assert.Equal(HttpStatusCode.OK, (await CancelAsync(flakyClient, companyId, employeeId, key)).StatusCode);

        Assert.Equal(fullEntitlement, await GetAnnualEntitlementAsync(companyId, employeeId));
        var cancelRows = (await GetPropagationsAsync(companyId, employeeId))
            .Where(p => p.OperationType == LeavingProcessPropagation.OperationCancelled).ToList();
        Assert.Single(cancelRows);
        Assert.Equal(LeavingProcessPropagation.StatusProcessed, cancelRows[0].Status);
    }

    [Fact]
    public async Task Consumers_AreIdempotent_WhenTheSameEventIsDeliveredTwice()
    {
        var companyId = Guid.NewGuid();
        using var client = await SeedClientAsync(companyId);
        var employeeId = await SeedEmployeeWithLeaveAsync(client, companyId);
        var leavingDate = NearLeavingDate;
        (await StartAsync(client, companyId, employeeId, leavingDate)).EnsureSuccessStatusCode();

        var reduced = await GetAnnualEntitlementAsync(companyId, employeeId);
        var tasksAfterStart = await GetOffboardingTasksAsync(companyId, employeeId);

        var newLeavingDate = leavingDate.AddDays(15);
        var setEvent = new EmployeeLeavingDateSetIntegrationEvent(
            companyId, employeeId, newLeavingDate, newLeavingDate.AddDays(-1), DateTimeOffset.UtcNow);

        for (var i = 0; i < 2; i++)
        {
            using var scope = _factory.Services.CreateScope();
            var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();
            Assert.True(await publisher.PublishAndConfirmAsync(setEvent, CancellationToken.None));
        }

        var afterTwiceSet = await GetAnnualEntitlementAsync(companyId, employeeId);
        Assert.True(afterTwiceSet > reduced);
        var tasksAfterSet = await GetOffboardingTasksAsync(companyId, employeeId);
        Assert.Equal(tasksAfterStart.Count, tasksAfterSet.Count);
        Assert.All(tasksAfterSet, t => Assert.Equal(newLeavingDate.AddDays(-1), t.DueDate));

        using (var scope = _factory.Services.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();
            Assert.True(await publisher.PublishAndConfirmAsync(setEvent, CancellationToken.None));
        }

        Assert.Equal(afterTwiceSet, await GetAnnualEntitlementAsync(companyId, employeeId));

        var cancelEvent = new EmployeeLeavingProcessCancelledIntegrationEvent(companyId, employeeId, DateTimeOffset.UtcNow);

        for (var i = 0; i < 2; i++)
        {
            using var scope = _factory.Services.CreateScope();
            var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();
            Assert.True(await publisher.PublishAndConfirmAsync(cancelEvent, CancellationToken.None));
        }

        var restored = await GetAnnualEntitlementAsync(companyId, employeeId);
        Assert.True(restored > afterTwiceSet);

        using (var scope = _factory.Services.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();
            Assert.True(await publisher.PublishAndConfirmAsync(cancelEvent, CancellationToken.None));
        }

        Assert.Equal(restored, await GetAnnualEntitlementAsync(companyId, employeeId));
        var tasksAfterCancel = await GetOffboardingTasksAsync(companyId, employeeId);
        Assert.Equal(tasksAfterStart.Count, tasksAfterCancel.Count);
        Assert.All(tasksAfterCancel, t => Assert.Equal("Cancelled", t.Status));
    }

    [Fact]
    public async Task Reconciliation_EnqueuesLegacyProcessesWithoutPropagationRows_AndConvergesLeaveBalance()
    {
        var companyId = Guid.NewGuid();
        using var client = await SeedClientAsync(companyId);
        var employeeId = await SeedEmployeeWithLeaveAsync(client, companyId);
        var fullEntitlement = await GetAnnualEntitlementAsync(companyId, employeeId);
        (await StartAsync(client, companyId, employeeId, NearLeavingDate)).EnsureSuccessStatusCode();

        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            await employeesDb.LeavingProcessPropagations
                .Where(p => p.CompanyId == companyId)
                .ExecuteDeleteAsync();

            var leaveDb = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var type = await leaveDb.LeaveTypes.SingleAsync(t => t.CompanyId == companyId && t.Code == "ANNUAL");
            var balance = await leaveDb.LeaveBalances.SingleAsync(b =>
                b.CompanyId == companyId && b.EmployeeId == employeeId && b.LeaveTypeId == type.Id
                && b.PolicyYear == DateTime.UtcNow.Year);
            balance.RecalculateEntitlement(fullEntitlement, balance.AccrualStartDate, DateTimeOffset.UtcNow);
            await leaveDb.SaveChangesAsync();
        }

        Assert.Equal(fullEntitlement, await GetAnnualEntitlementAsync(companyId, employeeId));
        Assert.Empty(await GetPropagationsAsync(companyId, employeeId));

        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ReconcileLeavingProcessPropagationsJob>().ExecuteAsync();
            await scope.ServiceProvider.GetRequiredService<ReconcileLeavingProcessPropagationsJob>().ExecuteAsync();
        }

        var rows = await GetPropagationsAsync(companyId, employeeId);
        var row = Assert.Single(rows);
        Assert.Equal(LeavingProcessPropagation.OperationReconciledStarted, row.OperationType);

        await RunDispatchJobAsync(_factory);

        Assert.Equal(LeavingProcessPropagation.StatusProcessed, (await GetPropagationsAsync(companyId, employeeId)).Single().Status);
        Assert.True(await GetAnnualEntitlementAsync(companyId, employeeId) < fullEntitlement);
    }

    [Fact]
    public async Task ListPropagations_ReportsPendingAndFailedCounts_AndRequiresAuthentication()
    {
        var companyId = Guid.NewGuid();
        using var seedClient = await SeedClientAsync(companyId);
        var employeeId = await SeedEmployeeWithLeaveAsync(seedClient, companyId);

        var failureSwitch = new ConsumerFailureSwitch { FailLeave = true };
        await using var flaky = CreateFlakyFactory(failureSwitch);
        using var flakyClient = ClientFor(flaky, companyId);
        (await StartAsync(flakyClient, companyId, employeeId, NearLeavingDate)).EnsureSuccessStatusCode();

        var response = await seedClient.GetAsync($"/api/companies/{companyId}/leaving-process-propagations?status=pending");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.Equal(1, payload!.PendingCount);
        var item = Assert.Single(payload.Items);
        Assert.Equal("started", item.OperationType);
        Assert.Equal(1, item.AttemptCount);
        Assert.NotNull(item.LastError);
        Assert.NotNull(item.NextAttemptAt);

        var invalid = await seedClient.GetAsync($"/api/companies/{companyId}/leaving-process-propagations?status=bogus");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);

        using var anonymous = _factory.CreateClient();
        var unauthorised = await anonymous.GetAsync($"/api/companies/{companyId}/leaving-process-propagations");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorised.StatusCode);

        failureSwitch.FailLeave = false;
        await MakeDueAsync(companyId, employeeId);
        await RunDispatchJobAsync(flaky);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record ListPayload(int PendingCount, int FailedCount, int ProcessedCount, List<ListItem> Items);

    private sealed record ListItem(string OperationType, int AttemptCount, string? LastError, DateTimeOffset? NextAttemptAt);
}
