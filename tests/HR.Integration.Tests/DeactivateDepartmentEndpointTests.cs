using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

public class DeactivateDepartmentEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public DeactivateDepartmentEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Delete_Department_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.DeleteAsync(
            $"/api/companies/{Guid.NewGuid()}/departments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Department_Returns_NotFound_When_Department_Does_Not_Exist()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "deact-dept-user-1");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/departments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Department_Deactivates_Active_Department()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "deact-dept-user-2");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        // Create
        var created = await client.PostAsJsonAsync($"/api/companies/{companyId}/departments",
            new { companyId, name = "Engineering" });
        created.EnsureSuccessStatusCode();
        var dept = await created.Content.ReadFromJsonAsync<DeptPayload>();
        Assert.NotNull(dept);

        // Deactivate
        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/departments/{dept!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify it no longer appears in the active list
        var list = await client.GetFromJsonAsync<DeptListPayload>(
            $"/api/companies/{companyId}/departments");
        Assert.NotNull(list);
        Assert.DoesNotContain(list!.Items, i => i.Id == dept.Id);
    }

    [Fact]
    public async Task Delete_Department_Returns_NotFound_When_Already_Inactive()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "deact-dept-user-3");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var created = await client.PostAsJsonAsync($"/api/companies/{companyId}/departments",
            new { companyId, name = "Engineering" });
        created.EnsureSuccessStatusCode();
        var dept = await created.Content.ReadFromJsonAsync<DeptPayload>();

        // First deactivation
        var first = await client.DeleteAsync(
            $"/api/companies/{companyId}/departments/{dept!.Id}");
        first.EnsureSuccessStatusCode();

        // Second deactivation — already inactive
        var second = await client.DeleteAsync(
            $"/api/companies/{companyId}/departments/{dept.Id}");

        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    private sealed record DeptPayload(Guid Id);
    private sealed record DeptListPayload(List<DeptItem> Items);
    private sealed record DeptItem(Guid Id);
}
