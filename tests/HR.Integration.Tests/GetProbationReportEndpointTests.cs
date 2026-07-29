using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class GetProbationReportEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public GetProbationReportEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientFor(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
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
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/probation");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_ProbationReport_Returns_Ok_For_HrAdministrator()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, companyId);

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
        using var client = ClientFor(userId, companyId);

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
        using var client = ClientFor(userId, Guid.Empty);

        var response = await client.GetAsync($"/api/companies/{Guid.Empty}/reporting/probation");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
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
