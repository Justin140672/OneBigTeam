using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

public class UpdateDepartmentEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public UpdateDepartmentEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
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
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "upd-dept-user-1");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/departments/{Guid.NewGuid()}",
            new { companyId, id = Guid.NewGuid(), name = "Engineering" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_Department_Updates_Name_And_Description()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "upd-dept-user-2");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        // Create
        var created = await client.PostAsJsonAsync($"/api/companies/{companyId}/departments", new
        {
            companyId,
            name = "Engineering"
        });
        created.EnsureSuccessStatusCode();
        var dept = await created.Content.ReadFromJsonAsync<DeptPayload>();
        Assert.NotNull(dept);

        // Update
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
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "upd-dept-user-3");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        // Create parent
        var parentResp = await client.PostAsJsonAsync($"/api/companies/{companyId}/departments", new
        {
            companyId,
            name = "Engineering"
        });
        parentResp.EnsureSuccessStatusCode();
        var parent = await parentResp.Content.ReadFromJsonAsync<DeptPayload>();

        // Create child
        var childResp = await client.PostAsJsonAsync($"/api/companies/{companyId}/departments", new
        {
            companyId,
            name = "Platform"
        });
        childResp.EnsureSuccessStatusCode();
        var child = await childResp.Content.ReadFromJsonAsync<DeptPayload>();
        Assert.NotNull(child);

        // Update child to have parent + manager
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
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "upd-dept-user-4");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

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
