using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// Integration coverage for the GetDepartment read slice
/// (GET /api/companies/{companyId}/departments/{id}).
/// </summary>
[Collection("Integration")]
public class GetDepartmentEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid AdminUser = new("dede0001-0000-0000-0000-000000000001");
    private static readonly Guid NoRoleUser = new("dede0001-0000-0000-0000-000000000002");

    public GetDepartmentEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUser, SystemRoles.HrAdministrator, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUser, SystemRoles.Employee, companyId);
        return client;
    }

    private async Task<Guid> CreateDepartmentAsync(HttpClient client, Guid companyId, string name, Guid? parentId = null)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/departments", new
        {
            companyId,
            name,
            description = "Owns a slice of the org",
            parentDepartmentId = parentId
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    [Fact]
    public async Task Get_Department_Returns_Unauthorized_For_Anonymous()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/departments/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Department_Returns_Forbidden_For_User_Without_Employee_Role()
    {
        var companyId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, NoRoleUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, NoRoleUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/departments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Department_Returns_Department_For_Authorized_User()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var parentId = await CreateDepartmentAsync(client, companyId, $"Parent-{Guid.NewGuid():N}");
        var departmentId = await CreateDepartmentAsync(client, companyId, $"Child-{Guid.NewGuid():N}", parentId);

        var response = await client.GetAsync($"/api/companies/{companyId}/departments/{departmentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<DepartmentPayload>();
        Assert.NotNull(payload);
        Assert.Equal(departmentId, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal("Owns a slice of the org", payload.Description);
        Assert.Equal(parentId, payload.ParentDepartmentId);
        Assert.True(payload.IsActive);
    }

    [Fact]
    public async Task Get_Department_Returns_NotFound_For_Unknown_Id()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/departments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Department_Returns_Forbidden_When_Route_Company_Does_Not_Match_Tenant()
    {
        var companyA = Guid.NewGuid();
        using var client = await AdminClient(companyA);
        var departmentId = await CreateDepartmentAsync(client, companyA, $"A-{Guid.NewGuid():N}");

        // Authenticated against company A but asking for it under a different company id.
        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/departments/{departmentId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Department_Returns_NotFound_For_Department_Owned_By_Another_Company()
    {
        // Department belongs to company B; caller is authorized for company B's own tenant but the
        // handler still scopes by CompanyId, so a company-A department id is invisible.
        var companyA = Guid.NewGuid();
        using var clientA = await AdminClient(companyA);
        var companyADepartmentId = await CreateDepartmentAsync(clientA, companyA, $"A-{Guid.NewGuid():N}");

        var companyB = Guid.NewGuid();
        using var clientB = await AdminClient(companyB);

        var response = await clientB.GetAsync($"/api/companies/{companyB}/departments/{companyADepartmentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record DepartmentPayload(
        Guid Id,
        Guid CompanyId,
        string Name,
        string? Description,
        Guid? ParentDepartmentId,
        Guid? ManagerEmployeeId,
        bool IsActive);
}
