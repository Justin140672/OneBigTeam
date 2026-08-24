using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// LEAVE-07: direct (non-draft) SubmitLeaveRequest auto-approves immediately when the employee's
// assigned leave policy has RequiresApproval = false, instead of leaving the request Pending for
// manual review.
[Collection("Integration")]
public class LeaveRequestApprovalPolicyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("cccccccc-0000-0000-0000-000000000005");

    private static readonly Guid SeededCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AnnualLeaveTypeId = Guid.Parse("A0000000-0000-0000-0000-000000000001");

    public LeaveRequestApprovalPolicyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Post_LeaveRequest_Auto_Approves_When_Policy_Does_Not_Require_Approval()
    {
        using var client = _factory.CreateClient();
        var companyId = SeededCompanyId;

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User1, SystemRoles.HrAdministrator, companyId);

        var policyResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new
            {
                companyId,
                name = $"Auto-approve Policy {Guid.NewGuid():N}",
                carryOverDays = 0,
                allowNegativeBalance = true,
                requiresApproval = false
            });
        policyResponse.EnsureSuccessStatusCode();
        var policy = await policyResponse.Content.ReadFromJsonAsync<PolicyPayload>();
        Assert.False(policy!.RequiresApproval);

        var employeeResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            new
            {
                companyId,
                firstName = "Auto",
                lastName = "Approved",
                workEmail = $"auto.approved.{Guid.NewGuid():N}@example.com",
                startDate = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender = "Male",
                employeeNumber = $"AA-{Guid.NewGuid():N}",
                employmentTypeId = Guid.Parse("40000000-0000-0000-0000-000000000001"),
                departmentId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                locationId = Guid.Parse("70000000-0000-0000-0000-000000000001"),
                positionProfileId = Guid.Parse("20000000-0000-0000-0000-000000000002")
            });
        employeeResponse.EnsureSuccessStatusCode();
        var employee = await employeeResponse.Content.ReadFromJsonAsync<EmployeePayload>();

        var assignResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee!.Id}/leave-policy",
            new
            {
                companyId,
                employeeId = employee.Id,
                leavePolicyId = policy.Id,
                effectiveFrom = "2026-01-01"
            });
        assignResponse.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/leave-requests",
            new
            {
                companyId,
                employeeId = employee.Id,
                leaveTypeId = AnnualLeaveTypeId,
                startDate = "2026-08-03",
                startPart = "FullDay",
                endDate = "2026-08-07",
                endPart = "FullDay",
                reason = "Auto approve test"
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<LeaveRequestPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Approved", payload!.Status);
    }

    private sealed record PolicyPayload(Guid Id, Guid CompanyId, string Name, bool RequiresApproval);
    private sealed record EmployeePayload(Guid Id, Guid CompanyId, string Status);
    private sealed record LeaveRequestPayload(Guid Id, Guid CompanyId, Guid EmployeeId, string Status);
}
