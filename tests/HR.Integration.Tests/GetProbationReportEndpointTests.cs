using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetProbationReportEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public GetProbationReportEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Get_ProbationReport_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/reporting/probation");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_ProbationReport_Returns_Forbidden_For_EmployeeOnly()
    {
        // "reporting:view-probation" grants baseline access to Manager or HrAdministrator only —
        // a plain Employee (no Manager, no HrAdministrator role) is outside the policy entirely.
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/probation");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_ProbationReport_Returns_Ok_For_HrAdministrator()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/probation");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReportPayload>();
        Assert.NotNull(payload);
        Assert.NotNull(payload!.Items);
    }

    [Fact]
    public async Task Get_ProbationReport_Returns_Empty_Not_CompanyWide_For_Manager_With_No_Direct_Reports()
    {
        // Row-level manager scoping (mirrors GetLeaveSummaryReport): the policy alone grants
        // baseline access to Manager, but the handler must hard-scope down to the caller's own
        // direct reports, never fall through to company-wide data.
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Manager);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/probation");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReportPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
        Assert.Equal(0, payload.CurrentProbationCount);
    }

    [Fact]
    public async Task Get_ProbationReport_Returns_UnprocessableEntity_For_Invalid_CompanyId()
    {
        // Tenant header must match the route companyId (Guid.Empty) so the request reaches the
        // validator rather than being rejected earlier as a cross-tenant mismatch — mirrors how
        // GetLeaveSummaryReportEndpointTests keeps the tenant header and route companyId aligned
        // for its validation-failure cases.
        var userId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, Guid.Empty);

        var response = await client.GetAsync($"/api/companies/{Guid.Empty}/reporting/probation");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Get_ProbationReport_For_Manager_Includes_Entire_Reporting_Hierarchy_Not_Just_Direct_Reports()
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

        await SeedProbationRecordAsync(companyId, leafEmployeeId, midManagerId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/probation");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReportPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, i => i.EmployeeId == leafEmployeeId);
    }

    [Fact]
    public async Task Get_ProbationReport_For_Manager_Excludes_Employees_Outside_Their_Hierarchy()
    {
        // The flip side of the multi-level test above: an unrelated manager (not an ancestor of
        // LeafEmployee anywhere in the tree) must not see LeafEmployee's probation record.
        var companyId = Guid.NewGuid();
        var topManagerId = Guid.NewGuid();
        var outsiderManagerId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, topManagerId, SystemRoles.Manager, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, outsiderManagerId, SystemRoles.Manager, companyId);

        await SeedEmployeeAsync(companyId, topManagerId, "Terry", "TopManager", null);
        var midManagerId = await SeedEmployeeAsync(companyId, Guid.NewGuid(), "Mia", "MidManager", topManagerId);
        var leafEmployeeId = await SeedEmployeeAsync(companyId, Guid.NewGuid(), "Leo", "LeafEmployee", midManagerId);
        await SeedEmployeeAsync(companyId, outsiderManagerId, "Ozzy", "Outsider", null);

        await SeedProbationRecordAsync(companyId, leafEmployeeId, midManagerId);

        using var outsiderClient = await ClientFor(outsiderManagerId, companyId);
        var response = await outsiderClient.GetAsync($"/api/companies/{companyId}/reporting/probation");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReportPayload>();
        Assert.NotNull(payload);
        Assert.DoesNotContain(payload!.Items, i => i.EmployeeId == leafEmployeeId);
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

    private async Task SeedProbationRecordAsync(Guid companyId, Guid employeeId, Guid managerEmployeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProbationDbContext>();
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, managerEmployeeId,
            today.AddMonths(-1), today.AddMonths(2), notes: null, today, now);
        db.ProbationRecords.Add(record);
        await db.SaveChangesAsync();
    }

    private sealed record ReportPayload(
        List<ReportItemPayload> Items,
        int CurrentProbationCount,
        int DueReviewCount,
        int OverdueReviewCount,
        int PassedCount,
        int ExtendedCount);

    private sealed record ReportItemPayload(
        Guid EmployeeId,
        string EmployeeName,
        string Status,
        DateOnly StartDate,
        DateOnly ExpectedEndDate,
        int DueReviews,
        int OverdueReviews);
}
