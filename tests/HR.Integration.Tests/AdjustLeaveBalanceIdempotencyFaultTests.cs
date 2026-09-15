using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.AdjustLeaveBalance;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 3 (P1) final gap item 5: proves AdjustLeaveBalanceHandler's two
/// <see cref="IPostCommitFaultInjector"/> call sites behave as their comments claim, using
/// <see cref="FaultInjectingPostCommitFaultInjector"/> (armed via
/// <see cref="ApiWebApplicationFactory.PostCommitFaultInjector"/>) to simulate each failure exactly
/// once for a specific Idempotency-Key. Setup helpers mirror AdjustLeaveBalanceEndpointTests.
/// </summary>
[Collection("Integration")]
public class AdjustLeaveBalanceIdempotencyFaultTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid HrAdminUser = new("d1d10002-0000-0000-0000-000000000001");

    public AdjustLeaveBalanceIdempotencyFaultTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();

        _factory.PostCommitFaultInjector.Reset();
    }

    [Fact]
    public async Task Post_AdjustLeaveBalance_PostCommitFault_Persists_Adjustment_And_Replays_On_Retry()
    {
        var (companyId, leaveTypeId, employeeId, client) = await SetupEmployeeWithBalanceAsync();
        var idempotencyKey = Guid.NewGuid();

        // Arm the double to fail AFTER the handler's own transaction commit (operation name has no
        // ".PreCommit" suffix) for this exact key.
        _factory.PostCommitFaultInjector.ArmOnce(nameof(AdjustLeaveBalanceHandler), idempotencyKey.ToString());

        var firstResponse = await SendAdjustAsync(client, companyId, employeeId, leaveTypeId, 2m, idempotencyKey);
        Assert.True((int)firstResponse.StatusCode >= 500);

        // The business write must have already committed despite the "failed" response.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            Assert.Single(await db.LeaveBalanceAdjustments.Where(a => a.CompanyId == companyId && a.EmployeeId == employeeId).ToListAsync());
        }

        // A retry with the SAME key and payload must replay the stored response rather than
        // double-apply the adjustment.
        var secondResponse = await SendAdjustAsync(client, companyId, employeeId, leaveTypeId, 2m, idempotencyKey);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            Assert.Single(await db.LeaveBalanceAdjustments.Where(a => a.CompanyId == companyId && a.EmployeeId == employeeId).ToListAsync());
        }
    }

    [Fact]
    public async Task Post_AdjustLeaveBalance_PreCommitFault_Persists_Nothing_And_Retry_Commits_Exactly_Once()
    {
        var (companyId, leaveTypeId, employeeId, client) = await SetupEmployeeWithBalanceAsync();
        var idempotencyKey = Guid.NewGuid();

        // Arm the double to fail BEFORE the transaction commits for this exact key.
        _factory.PostCommitFaultInjector.ArmOnce($"{nameof(AdjustLeaveBalanceHandler)}.PreCommit", idempotencyKey.ToString());

        var firstResponse = await SendAdjustAsync(client, companyId, employeeId, leaveTypeId, 2m, idempotencyKey);
        Assert.True((int)firstResponse.StatusCode >= 500);

        // Nothing committed on the failed first attempt.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            Assert.Empty(await db.LeaveBalanceAdjustments.Where(a => a.CompanyId == companyId && a.EmployeeId == employeeId).ToListAsync());
        }

        // Retry with the same key now goes through cleanly and commits exactly once.
        var secondResponse = await SendAdjustAsync(client, companyId, employeeId, leaveTypeId, 2m, idempotencyKey);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            Assert.Single(await db.LeaveBalanceAdjustments.Where(a => a.CompanyId == companyId && a.EmployeeId == employeeId).ToListAsync());
        }
    }

    // ── Helpers (mirrors AdjustLeaveBalanceEndpointTests' setup) ───────────────────

    private static Task<HttpResponseMessage> SendAdjustAsync(
        HttpClient client, Guid companyId, Guid employeeId, Guid leaveTypeId, decimal adjustmentValue, Guid idempotencyKey)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/companies/{companyId}/employees/{employeeId}/leave-balance-adjustments")
        {
            Content = JsonContent.Create(new
            {
                companyId,
                employeeId,
                leaveTypeId,
                adjustmentValue,
                reason = "Correction",
                comments = "Fault injection test adjustment",
                allowNegativeOverride = false,
            })
        };
        request.Headers.Add(IdempotentHttpClientExtensions.HeaderName, idempotencyKey.ToString());
        return client.SendAsync(request);
    }

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
                firstName = "Fault",
                lastName = "Tester",
                workEmail = $"fault.tester.{Guid.NewGuid():N}@example.com",
                startDate = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender = "Male",
                employeeNumber = $"FLT-{Guid.NewGuid():N}",
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
            $"/api/companies/{companyId}/departments",
            new { companyId, name = $"Dept-{Guid.NewGuid():N}" });
        deptResp.EnsureSuccessStatusCode();
        var departmentId = (await deptResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locTypeResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/location-types",
            new { companyId, name = $"LocType-{Guid.NewGuid():N}" });
        locTypeResp.EnsureSuccessStatusCode();
        var locationTypeId = (await locTypeResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/locations",
            new { companyId, name = $"Loc-{Guid.NewGuid():N}", locationTypeId });
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
            $"/api/companies/{companyId}/employment-types",
            new { companyId, name = $"EmpType-{Guid.NewGuid():N}" });
        etResp.EnsureSuccessStatusCode();
        var employmentTypeId = (await etResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        return (departmentId, locationId, positionProfileId, employmentTypeId);
    }

    private async Task<(Guid CompanyId, Guid LeaveTypeId, Guid EmployeeId, HttpClient HrAdminClient)> SetupEmployeeWithBalanceAsync(
        int defaultEntitlementDays = 25,
        bool allowNegativeBalance = false)
    {
        var companyId = Guid.NewGuid();
        var hrAdminClient = await AuthenticatedClient(HrAdminUser, companyId);

        var leaveTypeId = await CreateLeaveTypeAsync(companyId, defaultEntitlementDays);

        var policyResp = await hrAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new
            {
                companyId,
                name = $"Policy {Guid.NewGuid():N}",
                carryOverDays = 0,
                allowNegativeBalance
            });
        policyResp.EnsureSuccessStatusCode();
        var policy = await policyResp.Content.ReadFromJsonAsync<IdPayload>();

        var employeeId = await CreateEmployeeAsync(hrAdminClient, companyId);

        var assignResp = await hrAdminClient.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-policy",
            new { companyId, employeeId, leavePolicyId = policy!.Id, effectiveFrom = "2026-01-01" });
        assignResp.EnsureSuccessStatusCode();

        return (companyId, leaveTypeId, employeeId, hrAdminClient);
    }

    private sealed record IdPayload(Guid Id);
}
