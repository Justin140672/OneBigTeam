using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UpdateDepartmentEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("eeeeeeee-0000-0000-0000-000000000003");

    public UpdateDepartmentEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    private HttpClient AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    [Fact]
    public async Task Put_Department_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/departments/{Guid.NewGuid()}",
            new { name = "Engineering" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Department_Returns_NotFound_When_Department_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/departments/{Guid.NewGuid()}",
            new { companyId, id = Guid.NewGuid(), name = "Engineering" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_Department_Updates_Name_And_Description()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var created = await client.PostAsJsonAsync($"/api/companies/{companyId}/departments", new
        {
            companyId,
            name = "Engineering"
        });
        created.EnsureSuccessStatusCode();
        var dept = await created.Content.ReadFromJsonAsync<DeptPayload>();
        Assert.NotNull(dept);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/departments/{dept!.Id}",
            new
            {
                companyId,
                id = dept.Id,
                name = "Platform Engineering",
                description = "Builds the core platform"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<DeptPayload>();
        Assert.NotNull(payload);
        Assert.Equal(dept.Id, payload!.Id);
        Assert.Equal("Platform Engineering", payload.Name);
        Assert.Equal("Builds the core platform", payload.Description);
        Assert.True(payload.IsActive);
    }

    [Fact]
    public async Task Put_Department_Updates_Parent_And_Manager()
    {
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var parentResp = await client.PostAsJsonAsync($"/api/companies/{companyId}/departments", new
        {
            companyId,
            name = "Engineering"
        });
        parentResp.EnsureSuccessStatusCode();
        var parent = await parentResp.Content.ReadFromJsonAsync<DeptPayload>();

        var childResp = await client.PostAsJsonAsync($"/api/companies/{companyId}/departments", new
        {
            companyId,
            name = "Platform"
        });
        childResp.EnsureSuccessStatusCode();
        var child = await childResp.Content.ReadFromJsonAsync<DeptPayload>();
        Assert.NotNull(child);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/departments/{child!.Id}",
            new
            {
                companyId,
                id = child.Id,
                name = "Platform",
                parentDepartmentId = parent!.Id,
                managerEmployeeId = managerId
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<DeptPayload>();
        Assert.Equal(parent.Id, payload!.ParentDepartmentId);
        Assert.Equal(managerId, payload.ManagerEmployeeId);
    }

    [Fact]
    public async Task Put_Department_Returns_Conflict_When_Name_Already_Used()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        await client.PostAsJsonAsync($"/api/companies/{companyId}/departments",
            new { companyId, name = "Engineering" });
        var secondResp = await client.PostAsJsonAsync($"/api/companies/{companyId}/departments",
            new { companyId, name = "People" });
        var second = await secondResp.Content.ReadFromJsonAsync<DeptPayload>();
        Assert.NotNull(second);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/departments/{second!.Id}",
            new { companyId, id = second.Id, name = "Engineering" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private sealed record DeptPayload(
        Guid Id,
        Guid CompanyId,
        string Name,
        string? Description,
        Guid? ParentDepartmentId,
        Guid? ManagerEmployeeId,
        bool IsActive,
        DateTimeOffset UpdatedAt);
}
