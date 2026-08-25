using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetLeaveSummaryReportEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public GetLeaveSummaryReportEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> ClientFor(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee, companyId);
        return client;
    }

    [Fact]
    public async Task Get_LeaveSummary_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/reporting/leave-summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_LeaveSummary_Returns_Forbidden_For_Employee()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/leave-summary");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_LeaveSummary_Returns_Forbidden_For_Recruiter()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Recruiter);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/leave-summary");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_LeaveSummary_Returns_Ok_For_HrAdministrator()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/leave-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_LeaveSummary_Returns_Empty_Not_CompanyWide_For_Manager_With_No_Direct_Reports()
    {
        // Regression coverage for OBT-706's row-level manager scoping requirement: the policy
        // alone grants baseline access to Manager, but the handler must hard-scope down to the
        // caller's own direct reports (resolved from the "sub" claim), never fall through to
        // company-wide data — even though nothing here is forbidden at the policy layer.
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Manager);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/leave-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReportPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_LeaveSummary_Returns_UnprocessableEntity_For_Invalid_GroupBy()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/leave-summary?groupBy=999");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Get_LeaveSummary_Returns_UnprocessableEntity_For_PolicyYear_Out_Of_Range()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/leave-summary?policyYear=1900");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Theory]
    [InlineData("Department")]
    [InlineData("LeaveType")]
    public async Task Get_LeaveSummary_Accepts_All_GroupBy_Modes_For_HrAdministrator(string groupBy)
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/leave-summary?groupBy={groupBy}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_LeaveSummary_For_Manager_Includes_Entire_Reporting_Hierarchy_Not_Just_Direct_Reports()
    {
        // Regression coverage: the handler now scopes a Manager caller to their COMPLETE reporting
        // hierarchy (via IDirectReportsReader.GetAllDescendantIdsAsync) rather than only direct
        // reports (GetDirectReportIdsAsync). A 3-level chain — TopManager -> MidManager ->
        // LeafEmployee — proves the grandchild (2 levels deep) is visible to TopManager even though
        // LeafEmployee is not TopManager's direct report.
        var companyId = Guid.NewGuid();
        var topManagerId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, topManagerId, SystemRoles.Manager, companyId);
        using var client = await ClientFor(topManagerId, companyId);

        await SeedEmployeeAsync(companyId, topManagerId, "Terry", "TopManager", null);
        var midManagerId = await SeedEmployeeAsync(companyId, Guid.NewGuid(), "Mia", "MidManager", topManagerId);
        var leafEmployeeId = await SeedEmployeeAsync(companyId, Guid.NewGuid(), "Leo", "LeafEmployee", midManagerId);

        var leaveTypeId = await SeedLeaveTypeAsync(companyId);
        await SeedLeaveBalanceAsync(companyId, leafEmployeeId, leaveTypeId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/leave-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReportPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, i => i.GroupKey == leafEmployeeId.ToString());
    }

    [Fact]
    public async Task Get_LeaveSummary_For_Manager_Excludes_Employees_Outside_Their_Hierarchy()
    {
        // The flip side of the multi-level test above: an unrelated manager (not an ancestor of
        // LeafEmployee anywhere in the tree) must not see LeafEmployee's leave summary row.
        var companyId = Guid.NewGuid();
        var topManagerId = Guid.NewGuid();
        var outsiderManagerId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, topManagerId, SystemRoles.Manager, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, outsiderManagerId, SystemRoles.Manager, companyId);

        await SeedEmployeeAsync(companyId, topManagerId, "Terry", "TopManager", null);
        var midManagerId = await SeedEmployeeAsync(companyId, Guid.NewGuid(), "Mia", "MidManager", topManagerId);
        var leafEmployeeId = await SeedEmployeeAsync(companyId, Guid.NewGuid(), "Leo", "LeafEmployee", midManagerId);
        await SeedEmployeeAsync(companyId, outsiderManagerId, "Ozzy", "Outsider", null);

        var leaveTypeId = await SeedLeaveTypeAsync(companyId);
        await SeedLeaveBalanceAsync(companyId, leafEmployeeId, leaveTypeId);

        using var outsiderClient = await ClientFor(outsiderManagerId, companyId);
        var response = await outsiderClient.GetAsync($"/api/companies/{companyId}/reporting/leave-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReportPayload>();
        Assert.NotNull(payload);
        Assert.DoesNotContain(payload!.Items, i => i.GroupKey == leafEmployeeId.ToString());
    }

    private async Task<Guid> SeedEmployeeAsync(
        Guid companyId, Guid employeeId, string firstName, string lastName, Guid? managerId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var refData = await EmployeeReferenceDataSeeder.SeedAsync(db, companyId);
        var now = DateTimeOffset.UtcNow;
        var employee = Employee.Create(
            employeeId, companyId, firstName, lastName,
            $"{firstName}.{lastName}.{Guid.NewGuid():N}@example.com".ToLowerInvariant(),
            new DateOnly(2026, 1, 1), hasSystemAccess: true, new DateOnly(1990, 1, 1),
            "British", "Prefer not to say", $"EMP-{Guid.NewGuid():N}",
            refData.EmploymentTypeId, refData.DepartmentId, refData.LocationId, refData.PositionProfileId, now);
        if (managerId is not null)
            employee.Assign(employee.DepartmentId, employee.PositionProfileId, employee.LocationId, managerId, now);
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee.Id;
    }

    private async Task<Guid> SeedLeaveTypeAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var leaveType = LeaveType.Create(
            Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, DateTimeOffset.UtcNow);
        db.LeaveTypes.Add(leaveType);
        await db.SaveChangesAsync();
        return leaveType.Id;
    }

    private async Task SeedLeaveBalanceAsync(Guid companyId, Guid employeeId, Guid leaveTypeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var now = DateTimeOffset.UtcNow;
        var balance = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(), now.Year, 25m,
            new DateOnly(now.Year, 1, 1), now);
        db.LeaveBalances.Add(balance);
        await db.SaveChangesAsync();
    }

    private sealed record ReportPayload(List<ReportItemPayload> Items);

    private sealed record ReportItemPayload(
        string GroupKey,
        string GroupLabel,
        decimal EntitlementDays,
        decimal BookedDays,
        decimal ApprovedDays,
        decimal RemainingDays,
        int PendingRequestCount);
}
