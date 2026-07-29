using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class GetLeaveSummaryReportEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public GetLeaveSummaryReportEndpointTests(ApiWebApplicationFactory factory)
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
        using var client = ClientFor(userId, companyId);

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
        using var client = ClientFor(userId, companyId);

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
        using var client = ClientFor(userId, companyId);

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
        using var client = ClientFor(userId, companyId);

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
        using var client = ClientFor(userId, companyId);

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
        using var client = ClientFor(userId, companyId);

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
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/leave-summary?groupBy={groupBy}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
