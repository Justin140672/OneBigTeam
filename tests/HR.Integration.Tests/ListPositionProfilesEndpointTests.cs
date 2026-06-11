using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

public class ListPositionProfilesEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public ListPositionProfilesEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_PositionProfiles_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/position-profiles");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_PositionProfiles_Returns_Empty_List_When_None_Exist()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "list-pp-user-1");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.GetAsync($"/api/companies/{companyId}/position-profiles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<PositionProfilesListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_PositionProfiles_Returns_Created_Profiles_Alphabetically()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "list-pp-user-2");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var create1 = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            title = "Software Engineer",
            isManagerial = false
        });
        create1.EnsureSuccessStatusCode();

        var create2 = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            title = "Engineering Manager",
            isManagerial = true
        });
        create2.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/companies/{companyId}/position-profiles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<PositionProfilesListPayload>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Items.Count);
        Assert.Equal("Engineering Manager", payload.Items[0].Title);
        Assert.Equal("Software Engineer", payload.Items[1].Title);
    }

    [Fact]
    public async Task Get_PositionProfiles_Scopes_To_Company()
    {
        using var client = _factory.CreateClient();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "list-pp-user-3");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyA.ToString());

        var create = await client.PostAsJsonAsync($"/api/companies/{companyA}/position-profiles", new
        {
            companyId = companyA,
            title = "Analyst",
            isManagerial = false
        });
        create.EnsureSuccessStatusCode();

        using var clientB = _factory.CreateClient();
        clientB.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "list-pp-user-4");
        clientB.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyB.ToString());

        var response = await clientB.GetAsync($"/api/companies/{companyB}/position-profiles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<PositionProfilesListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    private sealed record PositionProfilesListPayload(IReadOnlyList<PositionProfileItem> Items);

    private sealed record PositionProfileItem(
        Guid Id,
        string? DepartmentName,
        string Title,
        string? Description,
        bool IsManagerial,
        bool IsActive);
}
