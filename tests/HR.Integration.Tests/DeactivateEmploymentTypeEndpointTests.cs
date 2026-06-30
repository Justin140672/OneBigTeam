using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class DeactivateEmploymentTypeEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("aa000004-0000-0000-0000-000000000001");

    public DeactivateEmploymentTypeEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    private HttpClient AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    [Fact]
    public async Task Delete_EmploymentType_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.DeleteAsync($"/api/companies/{Guid.NewGuid()}/employment-types/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_EmploymentType_Deactivates_It()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/employment-types", new
        {
            companyId,
            name = "Casual"
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<EmploymentTypePayload>();
        Assert.NotNull(created);

        var deleteResponse = await client.DeleteAsync(
            $"/api/companies/{companyId}/employment-types/{created!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listResponse = await client.GetAsync($"/api/companies/{companyId}/employment-types?isActive=false");
        var list = await listResponse.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(list);
        Assert.Contains(list!.Items, i => i.Id == created.Id && !i.IsActive);
    }

    [Fact]
    public async Task Delete_EmploymentType_Returns_NotFound_For_Unknown_Id()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/employment-types/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_EmploymentType_Returns_BadRequest_When_Already_Inactive()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/employment-types", new
        {
            companyId,
            name = "Apprentice"
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<EmploymentTypePayload>();
        Assert.NotNull(created);

        var first = await client.DeleteAsync($"/api/companies/{companyId}/employment-types/{created!.Id}");
        first.EnsureSuccessStatusCode();

        var second = await client.DeleteAsync($"/api/companies/{companyId}/employment-types/{created.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    private sealed record EmploymentTypePayload(Guid Id, string Name);

    private sealed record ListPayload(IReadOnlyList<ListItem> Items);

    private sealed record ListItem(Guid Id, string Name, bool IsActive);
}
