using System.Net;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class ExportOffboardingProgressReportEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public ExportOffboardingProgressReportEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Export_OffboardingProgressReport_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/reporting/offboarding-progress/export");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Export_OffboardingProgressReport_Returns_Forbidden_For_Manager()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Manager);
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/offboarding-progress/export");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Export_OffboardingProgressReport_Returns_Csv_For_HrAdministrator()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/offboarding-progress/export?format=Csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith(
            "Employee,Last Working Day,Status,Outstanding Tasks,Access Disabled,Documents Returned,Assets Returned",
            body);
    }

    [Fact]
    public async Task Export_OffboardingProgressReport_Returns_UnprocessableEntity_For_Invalid_Format()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/offboarding-progress/export?format=NotAFormat");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
