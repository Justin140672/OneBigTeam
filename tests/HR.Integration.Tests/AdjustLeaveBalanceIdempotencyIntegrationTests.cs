using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Leave;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using HR.SharedKernel.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 3 (P1) final follow-up items 4/7: proves the leave-adjustment idempotency contract
/// against a real PostgreSQL instance (via <see cref="ApiWebApplicationFactory"/>'s Testcontainer),
/// not EF's InMemory provider - the concurrent-race and restart-durability scenarios specifically
/// need a real database.
/// </summary>
[Collection("Integration")]
public class AdjustLeaveBalanceIdempotencyIntegrationTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid HrAdminUser = new("d1d10002-0000-0000-0000-000000000001");

    public AdjustLeaveBalanceIdempotencyIntegrationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Replaying_The_Same_Key_After_A_Lost_Response_Changes_The_Balance_Once()
    {
        var (companyId, leaveTypeId, employeeId, client) = await SetupEmployeeWithBalanceAsync();
        var idempotencyKey = Guid.NewGuid();
        var payload = AdjustmentPayload(companyId, employeeId, leaveTypeId, 2m, "Correction");

        // First delivery: the mutation, idempotency record and audit outbox entry all commit.
        var first = await SendAdjustmentAsync(client, companyId, employeeId, payload, idempotencyKey);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<AdjustmentPayloadResponse>();

        // Simulate losing that response and the caller (same unchanged request, same key) resending
        // through a BRAND NEW HttpRequestMessage - not a re-read of the first response.
        var second = await SendAdjustmentAsync(client, companyId, employeeId, payload, idempotencyKey);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var secondBody = await second.Content.ReadFromJsonAsync<AdjustmentPayloadResponse>();

        Assert.Equal(firstBody!.AdjustmentId, secondBody!.AdjustmentId);
        Assert.Equal(firstBody.NewRemainingHours, secondBody.NewRemainingHours);

        // Verify via a FRESH scope/DbContext (not any tracked entity from the calls above) that the
        // balance changed exactly once, one adjustment row exists, one idempotency record exists in
        // the correct scope, and one audit outbox entry exists.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();

        var balance = await db.LeaveBalances.SingleAsync(
            b => b.CompanyId == companyId && b.EmployeeId == employeeId && b.LeaveTypeId == leaveTypeId);
        Assert.Equal(27m, balance.RemainingDays); // 25 entitlement + 2 adjustment, applied ONCE

        Assert.Single(await db.LeaveBalanceAdjustments
            .Where(a => a.CompanyId == companyId && a.EmployeeId == employeeId).ToListAsync());

        var idempotencyRows = await db.IdempotencyRecords
            .Where(r => r.Key == idempotencyKey.ToString())
            .ToListAsync();
        var idempotencyRow = Assert.Single(idempotencyRows);
        Assert.Equal(companyId, idempotencyRow.CompanyId);
        Assert.Equal(HrAdminUser, idempotencyRow.ActorId);
        Assert.Equal("AdjustLeaveBalanceHandler", idempotencyRow.OperationId);

        Assert.Single(await db.AuditOutboxEntries.Where(e => e.CompanyId == companyId).ToListAsync());
    }

    [Fact]
    public async Task Reusing_A_Key_With_A_Changed_Payload_Returns_The_Documented_Conflict()
    {
        // Ticket 3 (P1) final follow-up item 6: proves the full client-to-API contract for a
        // changed request under a reused key - the header reaches the API, the operation/company/
        // actor scope is applied, and the mismatch is reported as a stable conflict rather than
        // silently replaying the wrong result or double-applying the adjustment.
        var (companyId, leaveTypeId, employeeId, client) = await SetupEmployeeWithBalanceAsync();
        var idempotencyKey = Guid.NewGuid();

        var first = await SendAdjustmentAsync(
            client, companyId, employeeId, AdjustmentPayload(companyId, employeeId, leaveTypeId, 2m, "Correction"),
            idempotencyKey);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Same key, but the user changed the adjustment amount - this must be rejected rather than
        // either replaying the first result or applying a second, different mutation.
        var second = await SendAdjustmentAsync(
            client, companyId, employeeId, AdjustmentPayload(companyId, employeeId, leaveTypeId, 5m, "Correction"),
            idempotencyKey);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var balance = await db.LeaveBalances.SingleAsync(
            b => b.CompanyId == companyId && b.EmployeeId == employeeId && b.LeaveTypeId == leaveTypeId);
        Assert.Equal(27m, balance.RemainingDays); // only the first (2-day) adjustment applied
        Assert.Single(await db.LeaveBalanceAdjustments
            .Where(a => a.CompanyId == companyId && a.EmployeeId == employeeId).ToListAsync());
    }

    [Fact]
    public async Task Concurrent_Delivery_Of_The_Same_Key_Only_Commits_One_Adjustment()
    {
        var (companyId, leaveTypeId, employeeId, client) = await SetupEmployeeWithBalanceAsync();
        var idempotencyKey = Guid.NewGuid();
        var payload = AdjustmentPayload(companyId, employeeId, leaveTypeId, 2m, "Correction");

        // Two independent, concurrently-in-flight deliveries of the exact same scoped key - proves
        // the Postgres unique-violation-then-replay race handling, which EF's InMemory provider
        // cannot exercise (no real transactions/constraints).
        var first = SendAdjustmentAsync(client, companyId, employeeId, payload, idempotencyKey);
        var second = SendAdjustmentAsync(client, companyId, employeeId, payload, idempotencyKey);
        var responses = await Task.WhenAll(first, second);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<AdjustmentPayloadResponse>()));
        Assert.Equal(bodies[0]!.AdjustmentId, bodies[1]!.AdjustmentId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();

        var balance = await db.LeaveBalances.SingleAsync(
            b => b.CompanyId == companyId && b.EmployeeId == employeeId && b.LeaveTypeId == leaveTypeId);
        Assert.Equal(27m, balance.RemainingDays);

        Assert.Single(await db.LeaveBalanceAdjustments
            .Where(a => a.CompanyId == companyId && a.EmployeeId == employeeId).ToListAsync());
        Assert.Single(await db.AuditOutboxEntries.Where(e => e.CompanyId == companyId).ToListAsync());
    }

    [Fact]
    public async Task Audit_Delivery_Failure_Is_Recovered_Without_Repeating_The_Mutation()
    {
        var (companyId, leaveTypeId, employeeId, client) = await SetupEmployeeWithBalanceAsync();
        var idempotencyKey = Guid.NewGuid();
        var payload = AdjustmentPayload(companyId, employeeId, leaveTypeId, 2m, "Correction");

        var response = await SendAdjustmentAsync(client, companyId, employeeId, payload, idempotencyKey);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // The handler's own inline dispatch (immediately after commit, via the real publisher)
        // already delivered this successfully - rewind it to "never delivered" so the failure
        // scenario below exercises genuine recovery rather than a no-op on an already-empty batch.
        using (var rewindScope = _factory.Services.CreateScope())
        {
            var db = rewindScope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var entry = await db.AuditOutboxEntries.SingleAsync(e => e.CompanyId == companyId);
            entry.DispatchedAt = null;
            entry.AttemptCount = 0;
            entry.NextAttemptAt = null;
            entry.LastError = null;
            await db.SaveChangesAsync();
        }

        // Run the dispatcher with a publisher that always fails - via a fresh scope/DbContext, so
        // this proves database durability rather than tracked-entity behaviour.
        using (var failScope = _factory.Services.CreateScope())
        {
            var db = failScope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var failingPublisher = new FailingAuditEventPublisher();
            await db.DispatchPendingAsync(
                db.AuditOutboxEntries, failingPublisher, DateTimeOffset.UtcNow, 200,
                NullLogger.Instance, CancellationToken.None);
        }

        using (var checkScope = _factory.Services.CreateScope())
        {
            var db = checkScope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var entry = await db.AuditOutboxEntries.SingleAsync(e => e.CompanyId == companyId);
            Assert.Null(entry.DispatchedAt);
            Assert.Equal(1, entry.AttemptCount);
            Assert.NotNull(entry.NextAttemptAt);
            Assert.False(entry.IsTerminallyFailed);

            // The mutation itself must remain single despite the audit failure.
            Assert.Single(await db.LeaveBalanceAdjustments
                .Where(a => a.CompanyId == companyId && a.EmployeeId == employeeId).ToListAsync());
        }

        // Now let delivery succeed, ignoring the backoff (NextAttemptAt) by passing "now" far
        // enough in the future - the dispatcher only cares that now >= NextAttemptAt. This batch
        // may also pick up other tests' pending rows (the outbox table is shared across this whole
        // collection's Postgres fixture), so isolate on CompanyId - not a global publish count.
        var publishedCompanyIds = new List<Guid>();
        using (var successScope = _factory.Services.CreateScope())
        {
            var db = successScope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var succeedingPublisher = new CountingAuditEventPublisher(evt =>
            {
                if (evt is LeaveBalanceAdjustedAuditEvent adjusted)
                    publishedCompanyIds.Add(adjusted.CompanyId);
            });
            await db.DispatchPendingAsync(
                db.AuditOutboxEntries, succeedingPublisher, DateTimeOffset.UtcNow.AddMinutes(10), 200,
                NullLogger.Instance, CancellationToken.None);
        }

        // Published exactly once for THIS test's own company in this pass.
        Assert.Single(publishedCompanyIds, id => id == companyId);

        using (var finalScope = _factory.Services.CreateScope())
        {
            var db = finalScope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var entry = await db.AuditOutboxEntries.SingleAsync(e => e.CompanyId == companyId);
            Assert.NotNull(entry.DispatchedAt);

            // Replay the original request (same key, unchanged payload) - it must return the
            // original response and must NOT enqueue a second outbox entry.
            var replay = await SendAdjustmentAsync(client, companyId, employeeId, payload, idempotencyKey);
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Single(await db.AuditOutboxEntries.Where(e => e.CompanyId == companyId).ToListAsync());
            Assert.Single(await db.LeaveBalanceAdjustments
                .Where(a => a.CompanyId == companyId && a.EmployeeId == employeeId).ToListAsync());
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static Task<HttpResponseMessage> SendAdjustmentAsync(
        HttpClient client, Guid companyId, Guid employeeId, object payload, Guid idempotencyKey)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/companies/{companyId}/employees/{employeeId}/leave-balance-adjustments")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Add(IdempotentHttpClientExtensions.HeaderName, idempotencyKey.ToString());
        return client.SendAsync(request);
    }

    private static object AdjustmentPayload(
        Guid companyId, Guid employeeId, Guid leaveTypeId, decimal adjustmentValue, string reason) => new
        {
            companyId,
            employeeId,
            leaveTypeId,
            adjustmentValue,
            reason,
            comments = "Idempotency integration test adjustment",
            allowNegativeOverride = false,
        };

    private async Task<HttpClient> AuthenticatedClient(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    private async Task<Guid> CreateLeaveTypeAsync(Guid companyId, int defaultEntitlementDays = 25)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var leaveTypeId = Guid.NewGuid();
        db.LeaveTypes.Add(LeaveType.Create(
            leaveTypeId, companyId, "Annual Leave", "ANNUAL", defaultEntitlementDays,
            AccrualMethod.None, LeaveTypeBehaviour.Standard, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        return leaveTypeId;
    }

    private static async Task<Guid> CreateEmployeeAsync(HttpClient client, Guid companyId)
    {
        var (departmentId, locationId, positionProfileId, employmentTypeId) =
            await CreateEmployeeReferenceDataAsync(client, companyId);

        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            new
            {
                companyId,
                firstName = "Idempotency",
                lastName = "Tester",
                workEmail = $"idempotency.tester.{Guid.NewGuid():N}@example.com",
                startDate = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender = "Male",
                employeeNumber = $"IDEM-{Guid.NewGuid():N}",
                employmentTypeId,
                departmentId,
                locationId,
                positionProfileId
            });
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<IdPayload>();
        return payload!.Id;
    }

    private static async Task<(Guid DepartmentId, Guid LocationId, Guid PositionProfileId, Guid EmploymentTypeId)>
        CreateEmployeeReferenceDataAsync(HttpClient client, Guid companyId)
    {
        var deptResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/departments", new { companyId, name = $"Dept-{Guid.NewGuid():N}" });
        deptResp.EnsureSuccessStatusCode();
        var departmentId = (await deptResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locTypeResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/location-types", new { companyId, name = $"LocType-{Guid.NewGuid():N}" });
        locTypeResp.EnsureSuccessStatusCode();
        var locationTypeId = (await locTypeResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/locations", new { companyId, name = $"Loc-{Guid.NewGuid():N}", locationTypeId });
        locResp.EnsureSuccessStatusCode();
        var locationId = (await locResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var leavePolicyResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"RefLeavePolicy-{Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false });
        leavePolicyResp.EnsureSuccessStatusCode();
        var defaultLeavePolicyId = (await leavePolicyResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var ppResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles",
            new { companyId, departmentId, locationId, title = $"Role-{Guid.NewGuid():N}", defaultLeavePolicyId });
        ppResp.EnsureSuccessStatusCode();
        var positionProfileId = (await ppResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var etResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employment-types", new { companyId, name = $"EmpType-{Guid.NewGuid():N}" });
        etResp.EnsureSuccessStatusCode();
        var employmentTypeId = (await etResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        return (departmentId, locationId, positionProfileId, employmentTypeId);
    }

    private async Task<(Guid CompanyId, Guid LeaveTypeId, Guid EmployeeId, HttpClient HrAdminClient)> SetupEmployeeWithBalanceAsync()
    {
        var companyId = Guid.NewGuid();
        var hrAdminClient = await AuthenticatedClient(HrAdminUser, companyId);

        var leaveTypeId = await CreateLeaveTypeAsync(companyId);

        var policyResp = await hrAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"Policy {Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false });
        policyResp.EnsureSuccessStatusCode();
        var policy = await policyResp.Content.ReadFromJsonAsync<IdPayload>();

        var employeeId = await CreateEmployeeAsync(hrAdminClient, companyId);

        var assignResp = await hrAdminClient.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-policy",
            new { companyId, employeeId, leavePolicyId = policy!.Id, effectiveFrom = "2026-01-01" });
        assignResp.EnsureSuccessStatusCode();

        return (companyId, leaveTypeId, employeeId, hrAdminClient);
    }

    private sealed class FailingAuditEventPublisher : IAuditEventPublisher
    {
        public Task PublishAsync<TAuditEvent>(TAuditEvent auditEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated audit publisher failure.");
    }

    private sealed class CountingAuditEventPublisher(Action<object> onPublish) : IAuditEventPublisher
    {
        public Task PublishAsync<TAuditEvent>(TAuditEvent auditEvent, CancellationToken cancellationToken)
        {
            onPublish(auditEvent!);
            return Task.CompletedTask;
        }
    }

    private sealed record IdPayload(Guid Id);

    private sealed record AdjustmentPayloadResponse(
        Guid AdjustmentId,
        Guid CompanyId,
        Guid EmployeeId,
        Guid LeaveTypeId,
        Guid LeaveBalanceId,
        decimal AdjustmentDays,
        decimal? AdjustmentHours,
        decimal NewRemainingDays,
        decimal NewRemainingHours,
        string Reason,
        string? Comments,
        Guid AdjustedByEmployeeId,
        DateTimeOffset AdjustedAt);
}
