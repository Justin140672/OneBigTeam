using System.Net;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class ExportEmployeeLeaverReportEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public ExportEmployeeLeaverReportEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Export_EmployeeLeavers_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/reporting/employee-leavers/export");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Export_EmployeeLeavers_Returns_Forbidden_For_Recruiter()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Recruiter);
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/employee-leavers/export");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Export_EmployeeLeavers_Returns_Csv_For_HrAdministrator()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/employee-leavers/export?format=Csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith(
            "Name,Leaving Date,Last Working Day,Department,Position,Reason,Offboarding Status,Account Status",
            body);
    }

    [Fact]
    public async Task Export_EmployeeLeavers_Returns_UnprocessableEntity_For_Invalid_DateRange()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/employee-leavers/export" +
            "?dateRangeStart=2026-06-01&dateRangeEnd=2026-01-01");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
