using System.Net.Http.Json;
using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Verifies the LEAVE-05 cross-module side effect: starting, amending and cancelling an employee's
/// leaving process (Employees module) publishes EmployeeLeavingDateSetIntegrationEvent /
/// EmployeeLeavingProcessCancelledIntegrationEvent, which LeavingDateChangeHandler in the Leave
/// module consumes to recalculate the employee's current-policy-year leave balance entitlement.
///
/// The employee's start date is deliberately set to well before the current calendar year, so the
/// only variable affecting the pro-rated entitlement across these assertions is the leaving date's
/// position within the current policy year — the assertions therefore stay valid regardless of what
/// "today" happens to be, except very close to a calendar year boundary (a risk already accepted by
/// other tests in this project, e.g. LeavingProcessLifecycleEndToEndTests).
/// </summary>
[Collection("Integration")]
public class LeavingDateChangeRecalculatesLeaveBalanceEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = new("ffffffff-3000-0000-0000-000000000001");

    public LeavingDateChangeRecalculatesLeaveBalanceEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUser, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    [Fact]
    public async Task Start_Amend_And_Cancel_LeavingProcess_Recalculate_The_Employees_Annual_Leave_Entitlement()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        // A real company gets its default leave types (incl. "ANNUAL") provisioned during
        // self-service signup (CompanyDefaultDataSeeder); this test bootstraps the company
        // directly, so do the same one step explicitly.
        using (var seedScope = _factory.Services.CreateScope())
        {
            await seedScope.ServiceProvider.GetRequiredService<ILeaveTypeDefaultsProvisioner>()
                .EnsureDefaultLeaveTypesAsync(companyId, default);
        }

        // Start date well before the current calendar year so the entitlement window's lower bound
        // is always the policy year start, not the employee's start date — isolating the leaving
        // date as the only variable under test.
        var employeeStartDate = new DateOnly(2020, 1, 1);
        var employeeResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Leaver", "Balance", $"leaver.balance.{Guid.NewGuid():N}@example.com",
                startDate: employeeStartDate));
        employeeResponse.EnsureSuccessStatusCode();
        var employeeId = (await employeeResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var policyResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"Test Policy {Guid.NewGuid():N}", carryOverDays = 5, allowNegativeBalance = false });
        policyResponse.EnsureSuccessStatusCode();
        var policyId = (await policyResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var assignResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-policy",
            new { companyId, employeeId, leavePolicyId = policyId, effectiveFrom = employeeStartDate.ToString("yyyy-MM-dd") });
        assignResponse.EnsureSuccessStatusCode();

        var entitlementBeforeLeaving = await GetAnnualLeaveEntitlementAsync(companyId, employeeId);
        Assert.Equal(25m, entitlementBeforeLeaving); // full default entitlement, no leaving process yet

        // ── Start the leaving process with a near-term leaving date ───────────────────────────
        var nearLeavingDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);
        var startResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                resignationReceivedDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
                leavingDate = nearLeavingDate.ToString("yyyy-MM-dd"),
                lastWorkingDay = nearLeavingDate.AddDays(-1).ToString("yyyy-MM-dd"),
                leavingReason = "Resignation"
            });
        startResp.EnsureSuccessStatusCode();

        var entitlementAfterStart = await GetAnnualLeaveEntitlementAsync(companyId, employeeId);
        Assert.True(entitlementAfterStart < entitlementBeforeLeaving,
            $"Expected entitlement to reduce below {entitlementBeforeLeaving} for a near-term leaving date, but was {entitlementAfterStart}.");

        // ── Amend the leaving date further out — entitlement should grow, not stack a reduction ─
        var laterLeavingDate = nearLeavingDate.AddDays(30);
        var amendResp = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                leavingDate = laterLeavingDate.ToString("yyyy-MM-dd"),
                lastWorkingDay = laterLeavingDate.AddDays(-1).ToString("yyyy-MM-dd"),
                leavingReason = "Resignation"
            });
        amendResp.EnsureSuccessStatusCode();

        var entitlementAfterAmend = await GetAnnualLeaveEntitlementAsync(companyId, employeeId);
        Assert.True(entitlementAfterAmend > entitlementAfterStart,
            $"Expected entitlement to grow above {entitlementAfterStart} after amending to a later leaving date, but was {entitlementAfterAmend}.");

        // ── Cancel the leaving process — entitlement should be restored to the pre-leaving figure ─
        var cancelResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process/cancel",
            new { companyId, employeeId, cancellationReason = "Employee retracted resignation." });
        cancelResp.EnsureSuccessStatusCode();

        var entitlementAfterCancel = await GetAnnualLeaveEntitlementAsync(companyId, employeeId);
        Assert.Equal(entitlementBeforeLeaving, entitlementAfterCancel);
    }

    private async Task<decimal> GetAnnualLeaveEntitlementAsync(Guid companyId, Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();

        var annualLeaveType = await db.LeaveTypes
            .SingleAsync(lt => lt.CompanyId == companyId && lt.Code == "ANNUAL");

        var currentPolicyYear = DateTime.UtcNow.Year;

        var balance = await db.LeaveBalances
            .SingleAsync(b => b.CompanyId == companyId
                            && b.EmployeeId == employeeId
                            && b.LeaveTypeId == annualLeaveType.Id
                            && b.PolicyYear == currentPolicyYear);

        return balance.EntitlementDays;
    }

    private sealed record IdPayload(Guid Id);
}
