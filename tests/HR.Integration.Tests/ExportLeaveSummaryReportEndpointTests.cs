using System.Net;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class ExportLeaveSummaryReportEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public ExportLeaveSummaryReportEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Export_LeaveSummary_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/reporting/leave-summary/export");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Export_LeaveSummary_Returns_Forbidden_For_Employee()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee);
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/leave-summary/export");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Export_LeaveSummary_Returns_Csv_For_Manager()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Manager);
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/leave-summary/export?format=Csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith(
            "Group,Entitlement Days,Booked Days,Approved Days,Remaining Days,Pending Requests", body);
    }

    [Fact]
    public async Task Export_LeaveSummary_Returns_UnprocessableEntity_For_Invalid_PolicyYear()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/leave-summary/export?policyYear=1900");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
