using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetSupportDashboardEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid EmployeeUserId = Guid.Parse("60000000-0000-0000-0000-000000000008");
    private static readonly Guid AdminUserId = Guid.Parse("60000000-0000-0000-0000-000000000009");

    public GetSupportDashboardEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> EmployeeClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, EmployeeUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, EmployeeUserId, SystemRoles.Employee, companyId);
        return client;
    }

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUserId, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    [Fact]
    public async Task Get_SupportDashboard_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/support/dashboard");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_SupportDashboard_Returns_Forbidden_For_Non_Staff_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = await EmployeeClient(companyId);

        var response = await client.GetAsync("/api/support/dashboard");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_SupportDashboard_Returns_Ok_For_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.GetAsync("/api/support/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<DashboardPayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.OpenRequestsCount >= 0);
    }

    private sealed record DashboardPayload(
        int OpenRequestsCount,
        double? AverageStaffResponseTimeHours,
        List<object> TopRequestedFeatures,
        List<object> TopReportedProblems,
        List<object> RequestsByType);
}
