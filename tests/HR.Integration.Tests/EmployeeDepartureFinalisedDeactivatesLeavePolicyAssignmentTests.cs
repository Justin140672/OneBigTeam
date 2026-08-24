using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Verifies the cross-module side effect closing the LEAVE-03 rollover gap: finalising an
/// employee's departure (via a backdated, confirmed leaving process — which
/// StartLeavingProcessHandler finalises synchronously, see EmployeeDepartureFinalizer) publishes
/// EmployeeDepartureFinalisedIntegrationEvent, which EmployeeDepartureFinalisedHandler in the Leave
/// module consumes to deactivate the employee's EmployeeLeavePolicyAssignment.
/// </summary>
[Collection("Integration")]
public class EmployeeDepartureFinalisedDeactivatesLeavePolicyAssignmentTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = new("ffffffff-2000-0000-0000-000000000001");

    public EmployeeDepartureFinalisedDeactivatesLeavePolicyAssignmentTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Backdated_Confirmed_Leaving_Process_Deactivates_Employee_Leave_Policy_Assignment()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUser, SystemRoles.HrAdministrator, companyId);

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        var employeeResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Departing", "Employee", $"departing.{Guid.NewGuid():N}@example.com"));
        employeeResponse.EnsureSuccessStatusCode();
        var employeeId = (await employeeResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var policyResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"Test Policy {Guid.NewGuid():N}", carryOverDays = 5, allowNegativeBalance = false });
        policyResponse.EnsureSuccessStatusCode();
        var policyId = (await policyResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var assignResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-policy",
            new { companyId, employeeId, leavePolicyId = policyId, effectiveFrom = "2026-01-01" });
        assignResponse.EnsureSuccessStatusCode();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var assignmentBeforeFinalisation = await db.EmployeeLeavePolicyAssignments
                .SingleAsync(a => a.CompanyId == companyId && a.EmployeeId == employeeId);
            Assert.True(assignmentBeforeFinalisation.IsActive);
        }

        // Backdated + confirmed LeavingDate finalises the employee's departure synchronously
        // within the request (see StartLeavingProcessHandler).
        var leavingResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                resignationReceivedDate = "2019-12-01",
                leavingDate = "2020-01-01",
                lastWorkingDay = "2019-12-31",
                leavingReason = "Resignation",
                confirmBackdatedLeavingDate = true
            });
        Assert.Equal(HttpStatusCode.Created, leavingResponse.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var assignmentAfterFinalisation = await db.EmployeeLeavePolicyAssignments
                .SingleAsync(a => a.CompanyId == companyId && a.EmployeeId == employeeId);
            Assert.False(assignmentAfterFinalisation.IsActive);
            Assert.NotNull(assignmentAfterFinalisation.DeactivatedAt);
        }
    }

    private sealed record IdPayload(Guid Id);
}
