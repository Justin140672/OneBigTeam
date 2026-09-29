using System.Net;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class PolicyBasedAuthorizationEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public PolicyBasedAuthorizationEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> AuthenticatedClient(Guid companyId, Guid userId, Guid roleId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, roleId, companyId);
        return client;
    }


    [Fact]
    public async Task Get_EmployeeAssets_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{Guid.NewGuid()}/assets");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_EmployeeAssets_Returns_Forbidden_For_Role_Without_AssetView_Permission()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId, Guid.NewGuid(), SystemRoles.Recruiter);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{Guid.NewGuid()}/assets");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_EmployeeAssets_Returns_OK_For_Role_With_AssetView_Permission()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId, userId, SystemRoles.Employee);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{userId}/assets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }


    [Fact]
    public async Task Get_LeavePolicies_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();

        var response = await client.GetAsync($"/api/companies/{companyId}/leave-policies");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_LeavePolicies_Returns_Forbidden_For_Role_Without_LeaveApprove_Permission()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId, Guid.NewGuid(), SystemRoles.Employee);

        var response = await client.GetAsync($"/api/companies/{companyId}/leave-policies");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_LeavePolicies_Returns_OK_For_Role_With_LeaveApprove_Permission()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId, Guid.NewGuid(), SystemRoles.Manager);

        var response = await client.GetAsync($"/api/companies/{companyId}/leave-policies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
