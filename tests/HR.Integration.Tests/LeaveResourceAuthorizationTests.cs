using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// LEAVE-01: resource-level (self / manager-hierarchy / HR-admin) authorization for the nine Leave
/// endpoints guarded by <c>HR.Modules.Leave.Services.LeaveResourceAuthorizer</c>. Endpoint-level
/// Policies(...) only prove tenant/role membership; they never prove the caller has a relationship
/// to the specific employeeId in the route, so these tests exercise that resource-ownership check
/// end-to-end over real HTTP, mirroring CompleteTaskAuthorizationTests's pattern for SEC-003.
/// </summary>
[Collection("Integration")]
public class LeaveResourceAuthorizationTests(ApiWebApplicationFactory factory)
{
    // Pre-seeded company/leave type used by SubmitLeaveRequestEndpointTests (see
    // LeaveModule.SeedLeaveAsync) — reused here to avoid re-seeding leave-type/reference data per test.
    private static readonly Guid SeededCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AnnualLeaveTypeId = Guid.Parse("A0000000-0000-0000-0000-000000000001");
    private static readonly Guid EmploymentTypeId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid DepartmentId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid LocationId = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly Guid PositionProfileId = Guid.Parse("20000000-0000-0000-0000-000000000002");

    private static readonly Guid OtherCompanyId = Guid.NewGuid();

    // ─────────────────────────────────────────────────────────────────────────
    // Self-service group: Submit / Preview / Cancel (CanActOnOwnLeaveAsync)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Submit_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{Guid.NewGuid()}/leave-requests",
            SubmitBody(AnnualLeaveTypeId, "2026-09-01", "2026-09-01"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Submit_Allows_Employee_Submitting_Own_Leave()
    {
        var employee = await CreateEmployeeAsync();
        await AssignPolicyAsync(employee);

        using var client = await AuthenticatedClient(employee);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests",
            SubmitBody(AnnualLeaveTypeId, "2026-09-01", "2026-09-01"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Submit_Allows_HrAdministrator_On_Behalf_Of_Employee()
    {
        var employee = await CreateEmployeeAsync();
        await AssignPolicyAsync(employee);

        using var client = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests",
            SubmitBody(AnnualLeaveTypeId, "2026-09-02", "2026-09-02"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Submit_Returns_Forbidden_For_Unrelated_Peer_Employee()
    {
        var employee = await CreateEmployeeAsync();
        var peer = await CreateEmployeeAsync();

        using var client = await AuthenticatedClient(peer);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests",
            SubmitBody(AnnualLeaveTypeId, "2026-09-03", "2026-09-03"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Submit_Returns_Forbidden_For_Direct_Manager_Submitting_On_Behalf_Of_Report()
    {
        // LEAVE-01: managers get view/approve access only, never self-service actions on behalf
        // of a report — submitting/previewing/cancelling leave "as" someone else is HR-admin only.
        var manager = await CreateEmployeeAsync();
        var report = await CreateEmployeeAsync();

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, report, manager);
        }

        using var client = await AuthenticatedClient(manager);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{report}/leave-requests",
            SubmitBody(AnnualLeaveTypeId, "2026-09-04", "2026-09-04"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Preview_Returns_Forbidden_For_Manager_Previewing_On_Behalf_Of_Report()
    {
        var manager = await CreateEmployeeAsync();
        var report = await CreateEmployeeAsync();

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, report, manager);
        }

        using var client = await AuthenticatedClient(manager);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{report}/leave-requests/preview",
            new
            {
                companyId = SeededCompanyId,
                employeeId = report,
                leaveTypeId = AnnualLeaveTypeId,
                startDate = "2026-09-05",
                startPart = "FullDay",
                endDate = "2026-09-05",
                endPart = "FullDay"
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Preview_Allows_Employee_Previewing_Own_Leave()
    {
        var employee = await CreateEmployeeAsync();
        await AssignPolicyAsync(employee);

        using var client = await AuthenticatedClient(employee);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests/preview",
            new
            {
                companyId = SeededCompanyId,
                employeeId = employee,
                leaveTypeId = AnnualLeaveTypeId,
                startDate = "2026-09-06",
                startPart = "FullDay",
                endDate = "2026-09-06",
                endPart = "FullDay"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Preview_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{Guid.NewGuid()}/leave-requests/preview",
            new
            {
                companyId = SeededCompanyId,
                employeeId = Guid.NewGuid(),
                leaveTypeId = AnnualLeaveTypeId,
                startDate = "2026-09-06",
                startPart = "FullDay",
                endDate = "2026-09-06",
                endPart = "FullDay"
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_Allows_Employee_Cancelling_Own_Leave_Request()
    {
        var employee = await CreateEmployeeAsync();
        await AssignPolicyAsync(employee);
        using var client = await AuthenticatedClient(employee);

        var leaveRequestId = await SubmitAsync(client, employee, "2026-09-07", "2026-09-07");

        var response = await client.DeleteAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests/{leaveRequestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_Returns_Forbidden_For_Manager_Cancelling_On_Behalf_Of_Report()
    {
        var employee = await CreateEmployeeAsync();
        await AssignPolicyAsync(employee);
        var manager = await CreateEmployeeAsync();

        using var employeeClient = await AuthenticatedClient(employee);
        var leaveRequestId = await SubmitAsync(employeeClient, employee, "2026-09-08", "2026-09-08");

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, employee, manager);
        }

        using var managerClient = await AuthenticatedClient(manager);

        var response = await managerClient.DeleteAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests/{leaveRequestId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_Returns_Forbidden_For_Cross_Company_Employee()
    {
        var employee = await CreateEmployeeAsync();
        using var client = await AuthenticatedClient(employee);

        // A caller from a different company attempting to act on this employeeId — the
        // authorizer's self-check compares raw ids only, so cross-tenant callers are denied via
        // the peer/manager-hierarchy path (they are never self, HR-admin, or an in-hierarchy
        // manager of a resource in a company they aren't a member of).
        var crossCompanyCaller = Guid.NewGuid();
        using var crossClient = factory.CreateClient();
        crossClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, crossCompanyCaller.ToString());
        crossClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, OtherCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(factory, crossCompanyCaller, SystemRoles.Employee, OtherCompanyId);

        var response = await crossClient.DeleteAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // View group: Get / List / GetEmployeeLeaveBalance (CanViewAsync)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_LeaveRequest_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{Guid.NewGuid()}/leave-requests/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_LeaveRequest_Allows_Employee_Viewing_Own_Request()
    {
        var employee = await CreateEmployeeAsync();
        await AssignPolicyAsync(employee);
        using var client = await AuthenticatedClient(employee);

        var leaveRequestId = await SubmitAsync(client, employee, "2026-09-09", "2026-09-09");

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests/{leaveRequestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_LeaveRequest_Allows_Direct_Manager()
    {
        var employee = await CreateEmployeeAsync();
        await AssignPolicyAsync(employee);
        var manager = await CreateEmployeeAsync();

        using var employeeClient = await AuthenticatedClient(employee);
        var leaveRequestId = await SubmitAsync(employeeClient, employee, "2026-09-10", "2026-09-10");

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, employee, manager);
        }

        using var managerClient = await AuthenticatedClient(manager);

        var response = await managerClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests/{leaveRequestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_LeaveRequest_Allows_Skip_Level_Manager_In_Three_Level_Hierarchy()
    {
        var seniorManager = await CreateEmployeeAsync(); // C
        var manager = await CreateEmployeeAsync();       // B
        var employee = await CreateEmployeeAsync();      // A
        await AssignPolicyAsync(employee);

        using var employeeClient = await AuthenticatedClient(employee);
        var leaveRequestId = await SubmitAsync(employeeClient, employee, "2026-09-11", "2026-09-11");

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, employee, manager);
            await AssignManagerAsync(setupClient, manager, seniorManager);
        }

        using var seniorManagerClient = await AuthenticatedClient(seniorManager);

        var response = await seniorManagerClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests/{leaveRequestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_LeaveRequest_Allows_HrAdministrator()
    {
        var employee = await CreateEmployeeAsync();
        await AssignPolicyAsync(employee);
        using var employeeClient = await AuthenticatedClient(employee);
        var leaveRequestId = await SubmitAsync(employeeClient, employee, "2026-09-12", "2026-09-12");

        using var hrClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var response = await hrClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests/{leaveRequestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_LeaveRequest_Returns_Forbidden_For_Unrelated_Peer_Employee()
    {
        var employee = await CreateEmployeeAsync();
        await AssignPolicyAsync(employee);
        var peer = await CreateEmployeeAsync();

        using var employeeClient = await AuthenticatedClient(employee);
        var leaveRequestId = await SubmitAsync(employeeClient, employee, "2026-09-13", "2026-09-13");

        using var peerClient = await AuthenticatedClient(peer);

        var response = await peerClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests/{leaveRequestId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_LeaveRequest_Returns_Forbidden_For_Own_Manager_Viewed_Bottom_Up()
    {
        // Denial case: being someone's report does not grant you view rights over your manager's
        // resources — the hierarchy check is one-directional (manager -> report only).
        var manager = await CreateEmployeeAsync();
        var report = await CreateEmployeeAsync();
        await AssignPolicyAsync(manager);

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, report, manager);
        }

        using var managerClient = await AuthenticatedClient(manager);
        var leaveRequestId = await SubmitAsync(managerClient, manager, "2026-09-14", "2026-09-14");

        using var reportClient = await AuthenticatedClient(report);

        var response = await reportClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{manager}/leave-requests/{leaveRequestId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_LeaveRequests_Allows_Employee_Viewing_Own_List()
    {
        var employee = await CreateEmployeeAsync();
        using var client = await AuthenticatedClient(employee);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task List_LeaveRequests_Returns_Forbidden_For_Unrelated_Peer_Employee()
    {
        var employee = await CreateEmployeeAsync();
        var peer = await CreateEmployeeAsync();
        using var client = await AuthenticatedClient(peer);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_LeaveRequests_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{Guid.NewGuid()}/leave-requests");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetEmployeeLeaveBalance_Allows_Direct_Manager()
    {
        var manager = await CreateEmployeeAsync();
        var report = await CreateEmployeeAsync();

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, report, manager);
        }

        using var client = await AuthenticatedClient(manager);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{report}/leave-balances?policyYear=2026");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetEmployeeLeaveBalance_Returns_Forbidden_For_Unrelated_Peer_Employee()
    {
        var employee = await CreateEmployeeAsync();
        var peer = await CreateEmployeeAsync();
        using var client = await AuthenticatedClient(peer);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-balances?policyYear=2026");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetEmployeeLeaveBalance_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{Guid.NewGuid()}/leave-balances?policyYear=2026");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetEmployeeLeaveBalance_Returns_Forbidden_For_Cross_Company_Caller()
    {
        var employee = await CreateEmployeeAsync();

        var crossCompanyCaller = Guid.NewGuid();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, crossCompanyCaller.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, OtherCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(factory, crossCompanyCaller, SystemRoles.Employee, OtherCompanyId);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-balances?policyYear=2026");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // GetLeaveBalanceHistory now carries "role:employee" at the FastEndpoints level (matching
    // GetEmployeeLeaveBalance), so the resource-authorization check in LeaveResourceAuthorizer.
    // CanViewAsync (self / manager-in-hierarchy / HR-admin) is reachable by managers too.

    [Fact]
    public async Task GetLeaveBalanceHistory_Allows_Employee_Viewing_Own_History()
    {
        var employee = await CreateEmployeeAsync();
        using var client = await AuthenticatedClient(employee);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-types/{AnnualLeaveTypeId}/balance-history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetLeaveBalanceHistory_Allows_Direct_Manager()
    {
        var manager = await CreateEmployeeAsync();
        var report = await CreateEmployeeAsync();

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, report, manager);
        }

        using var client = await AuthenticatedClient(manager);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{report}/leave-types/{AnnualLeaveTypeId}/balance-history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetLeaveBalanceHistory_Returns_Forbidden_For_Unrelated_Peer_Employee()
    {
        var employee = await CreateEmployeeAsync();
        var peer = await CreateEmployeeAsync();
        using var client = await AuthenticatedClient(peer);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-types/{AnnualLeaveTypeId}/balance-history");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetLeaveBalanceHistory_Allows_HrAdministrator()
    {
        var employee = await CreateEmployeeAsync();
        using var client = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-types/{AnnualLeaveTypeId}/balance-history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetLeaveBalanceHistory_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{Guid.NewGuid()}/leave-types/{AnnualLeaveTypeId}/balance-history");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Approve/Reject group (CanApproveOrRejectAsync) + reviewer-identity spoofing
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Approve_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{Guid.NewGuid()}/leave-requests/{Guid.NewGuid()}/approve",
            new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Approve_Allows_Direct_Manager()
    {
        var employee = await CreateEmployeeAsync();
        await AssignPolicyAsync(employee);
        var manager = await CreateEmployeeAsync();

        using var employeeClient = await AuthenticatedClient(employee);
        var leaveRequestId = await SubmitAsync(employeeClient, employee, "2026-09-15", "2026-09-15");

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, employee, manager);
        }

        using var managerClient = await AuthenticatedClient(manager);

        var response = await managerClient.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests/{leaveRequestId}/approve",
            new { companyId = SeededCompanyId, employeeId = employee, leaveRequestId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Approve_Allows_HrAdministrator()
    {
        var employee = await CreateEmployeeAsync();
        await AssignPolicyAsync(employee);

        using var employeeClient = await AuthenticatedClient(employee);
        var leaveRequestId = await SubmitAsync(employeeClient, employee, "2026-09-16", "2026-09-16");

        using var hrClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var response = await hrClient.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests/{leaveRequestId}/approve",
            new { companyId = SeededCompanyId, employeeId = employee, leaveRequestId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Approve_Returns_Forbidden_For_Unrelated_Peer_Manager()
    {
        var employee = await CreateEmployeeAsync();
        await AssignPolicyAsync(employee);
        var otherManager = await CreateEmployeeAsync();

        using var employeeClient = await AuthenticatedClient(employee);
        var leaveRequestId = await SubmitAsync(employeeClient, employee, "2026-09-17", "2026-09-17");

        using var otherManagerClient = await AuthenticatedClient(otherManager);

        var response = await otherManagerClient.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests/{leaveRequestId}/approve",
            new { companyId = SeededCompanyId, employeeId = employee, leaveRequestId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Approve_Returns_Forbidden_For_Self_Approval()
    {
        // CanApproveOrRejectAsync has no self path — an employee (even one holding the manager
        // role) can never approve their own leave request.
        var employee = await CreateEmployeeAsync();
        await AssignPolicyAsync(employee);

        using var client = await AuthenticatedClient(employee, manager: true);
        var leaveRequestId = await SubmitAsync(client, employee, "2026-09-18", "2026-09-18");

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests/{leaveRequestId}/approve",
            new { companyId = SeededCompanyId, employeeId = employee, leaveRequestId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Approve_Returns_Forbidden_For_Cross_Company_Manager()
    {
        var employee = await CreateEmployeeAsync();
        await AssignPolicyAsync(employee);

        using var employeeClient = await AuthenticatedClient(employee);
        var leaveRequestId = await SubmitAsync(employeeClient, employee, "2026-09-19", "2026-09-19");

        var crossCompanyCaller = Guid.NewGuid();
        using var crossClient = factory.CreateClient();
        crossClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, crossCompanyCaller.ToString());
        crossClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, OtherCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(factory, crossCompanyCaller, SystemRoles.Manager, OtherCompanyId);

        var response = await crossClient.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests/{leaveRequestId}/approve",
            new { companyId = SeededCompanyId, employeeId = employee, leaveRequestId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Approve_Ignores_Client_Supplied_ReviewedByEmployeeId_And_Uses_Authenticated_Caller()
    {
        // SEC: ReviewedByEmployeeId supplied in the request body must never be trusted — an
        // impersonation attempt (claiming someone else approved) must be silently overwritten
        // with the authenticated caller's own id before authorization or persistence.
        var employee = await CreateEmployeeAsync();
        await AssignPolicyAsync(employee);
        var manager = await CreateEmployeeAsync();
        var impersonationTarget = Guid.NewGuid();

        using var employeeClient = await AuthenticatedClient(employee);
        var leaveRequestId = await SubmitAsync(employeeClient, employee, "2026-09-20", "2026-09-20");

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, employee, manager);
        }

        using var managerClient = await AuthenticatedClient(manager);

        var response = await managerClient.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests/{leaveRequestId}/approve",
            new
            {
                companyId = SeededCompanyId,
                employeeId = employee,
                leaveRequestId,
                reviewedByEmployeeId = impersonationTarget
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApprovalPayload>();
        Assert.NotNull(payload);
        Assert.Equal(manager, payload!.ReviewedByEmployeeId);
        Assert.NotEqual(impersonationTarget, payload.ReviewedByEmployeeId);
    }

    [Fact]
    public async Task Reject_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{Guid.NewGuid()}/leave-requests/{Guid.NewGuid()}/reject",
            new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reject_Allows_Direct_Manager()
    {
        var employee = await CreateEmployeeAsync();
        await AssignPolicyAsync(employee);
        var manager = await CreateEmployeeAsync();

        using var employeeClient = await AuthenticatedClient(employee);
        var leaveRequestId = await SubmitAsync(employeeClient, employee, "2026-09-21", "2026-09-21");

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, employee, manager);
        }

        using var managerClient = await AuthenticatedClient(manager);

        var response = await managerClient.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests/{leaveRequestId}/reject",
            new { companyId = SeededCompanyId, employeeId = employee, leaveRequestId, rejectionReason = "Team conflict" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Reject_Returns_Forbidden_For_Unrelated_Peer_Manager()
    {
        var employee = await CreateEmployeeAsync();
        await AssignPolicyAsync(employee);
        var otherManager = await CreateEmployeeAsync();

        using var employeeClient = await AuthenticatedClient(employee);
        var leaveRequestId = await SubmitAsync(employeeClient, employee, "2026-09-22", "2026-09-22");

        using var otherManagerClient = await AuthenticatedClient(otherManager);

        var response = await otherManagerClient.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests/{leaveRequestId}/reject",
            new { companyId = SeededCompanyId, employeeId = employee, leaveRequestId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reject_Returns_Forbidden_For_Self_Rejection()
    {
        var employee = await CreateEmployeeAsync();
        await AssignPolicyAsync(employee);

        using var client = await AuthenticatedClient(employee, manager: true);
        var leaveRequestId = await SubmitAsync(client, employee, "2026-09-23", "2026-09-23");

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests/{leaveRequestId}/reject",
            new { companyId = SeededCompanyId, employeeId = employee, leaveRequestId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reject_Ignores_Client_Supplied_ReviewedByEmployeeId_And_Uses_Authenticated_Caller()
    {
        var employee = await CreateEmployeeAsync();
        await AssignPolicyAsync(employee);
        var manager = await CreateEmployeeAsync();
        var impersonationTarget = Guid.NewGuid();

        using var employeeClient = await AuthenticatedClient(employee);
        var leaveRequestId = await SubmitAsync(employeeClient, employee, "2026-09-24", "2026-09-24");

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, employee, manager);
        }

        using var managerClient = await AuthenticatedClient(manager);

        var response = await managerClient.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/leave-requests/{leaveRequestId}/reject",
            new
            {
                companyId = SeededCompanyId,
                employeeId = employee,
                leaveRequestId,
                reviewedByEmployeeId = impersonationTarget,
                rejectionReason = "Not enough cover"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<RejectionPayload>();
        Assert.NotNull(payload);
        Assert.Equal(manager, payload!.ReviewedByEmployeeId);
        Assert.NotEqual(impersonationTarget, payload.ReviewedByEmployeeId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<HttpClient> AuthenticatedClient(Guid userId, bool hrAdministrator = false, bool manager = false)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.Employee, SeededCompanyId);

        if (manager)
            await TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.Manager, SeededCompanyId);

        if (hrAdministrator)
            await TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.HrAdministrator, SeededCompanyId);

        return client;
    }

    /// <summary>
    /// Creates a real employee via the employees API and returns its id. An employee's id doubles
    /// as the identity user id for the linked account (see GetMyEmployeeHandler's `e.Id == userId`
    /// lookup), so this id is used both as the leave resource's EmployeeId and as the
    /// TestAuthHandler.UserHeader value when acting "as" that employee.
    /// </summary>
    private async Task<Guid> CreateEmployeeAsync()
    {
        using var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var unique = Guid.NewGuid().ToString("N")[..12];

        var response = await setupClient.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees",
            new
            {
                companyId = SeededCompanyId,
                firstName = "Test",
                lastName = $"Employee-{unique}",
                workEmail = $"leave.auth.{unique}@example.com",
                startDate = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender = "Male",
                employeeNumber = $"LEN-{unique}",
                employmentTypeId = EmploymentTypeId,
                departmentId = DepartmentId,
                locationId = LocationId,
                positionProfileId = PositionProfileId
            });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<EmployeePayload>();
        return payload!.Id;
    }

    private async Task AssignManagerAsync(HttpClient client, Guid employeeId, Guid managerId)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employeeId}/manager",
            new { companyId = SeededCompanyId, id = employeeId, managerId });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Assigns a leave policy with AllowNegativeBalance=true to the employee, so
    /// SubmitLeaveRequestHandler's balance check never blocks the submissions these authorization
    /// tests need to set up (they exist purely to exercise authorization, not balance rules).
    /// </summary>
    private async Task AssignPolicyAsync(Guid employeeId)
    {
        using var hrClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var policyResponse = await hrClient.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/leave-policies",
            new
            {
                companyId = SeededCompanyId,
                name = $"AuthTestPolicy-{Guid.NewGuid():N}",
                carryOverDays = 0,
                allowNegativeBalance = true
            });
        policyResponse.EnsureSuccessStatusCode();
        var policy = await policyResponse.Content.ReadFromJsonAsync<PolicyPayload>();

        var assignResponse = await hrClient.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employeeId}/leave-policy",
            new
            {
                companyId = SeededCompanyId,
                employeeId,
                leavePolicyId = policy!.Id,
                effectiveFrom = "2026-01-01"
            });
        assignResponse.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> SubmitAsync(HttpClient client, Guid employeeId, string startDate, string endDate)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employeeId}/leave-requests",
            SubmitBody(AnnualLeaveTypeId, startDate, endDate));
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Submit failed {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var payload = await response.Content.ReadFromJsonAsync<LeaveRequestPayload>();
        return payload!.Id;
    }

    private static object SubmitBody(Guid leaveTypeId, string startDate, string endDate) => new
    {
        companyId = SeededCompanyId,
        leaveTypeId,
        startDate,
        startPart = "FullDay",
        endDate,
        endPart = "FullDay"
    };

    private sealed record EmployeePayload(Guid Id);
    private sealed record PolicyPayload(Guid Id);
    private sealed record LeaveRequestPayload(Guid Id);
    private sealed record ApprovalPayload(Guid Id, Guid ReviewedByEmployeeId);
    private sealed record RejectionPayload(Guid Id, Guid ReviewedByEmployeeId);
}
