using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class UpdatePositionProfileEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("eeeeeeee-0000-0000-0000-000000000005");

    public UpdatePositionProfileEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Put_PositionProfile_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}/position-profiles/{Guid.NewGuid()}", new
        {
            title = "Some Title"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_PositionProfile_Updates_Profile()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            title = "Original Title"
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<PositionProfilePayload>();
        Assert.NotNull(created);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{created!.Id}", new
            {
                companyId,
                id = created.Id,
                title = "Updated Title",
                description = "Now with description"
            });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var payload = await updateResponse.Content.ReadFromJsonAsync<UpdatedPositionProfilePayload>();
        Assert.NotNull(payload);
        Assert.Equal("Updated Title", payload!.Title);
        Assert.Equal("Now with description", payload.Description);
    }

    [Fact]
    public async Task Put_PositionProfile_Returns_NotFound_For_Unknown_Id()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{Guid.NewGuid()}", new
            {
                companyId,
                id = Guid.NewGuid(),
                title = "Whatever"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_PositionProfile_Returns_Conflict_For_Duplicate_Title()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var create1 = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            title = "Profile A"
        });
        create1.EnsureSuccessStatusCode();

        var create2 = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            title = "Profile B"
        });
        create2.EnsureSuccessStatusCode();
        var profileB = await create2.Content.ReadFromJsonAsync<PositionProfilePayload>();
        Assert.NotNull(profileB);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{profileB!.Id}", new
            {
                companyId,
                id = profileB.Id,
                title = "Profile A"
            });

        Assert.Equal(HttpStatusCode.Conflict, updateResponse.StatusCode);
    }

    [Fact]
    public async Task Put_PositionProfile_Returns_Forbidden_When_Route_Company_Does_Not_Match_Auth_Tenant()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        using var client = AuthenticatedClient(companyA);

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyA}/position-profiles", new
        {
            companyId = companyA,
            title = "Designer"
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<PositionProfilePayload>();
        Assert.NotNull(created);

        // Authenticated as companyA but route targets companyB — middleware blocks it.
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyB}/position-profiles/{created!.Id}", new
            {
                companyId = companyB,
                id = created.Id,
                title = "Designer"
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record PositionProfilePayload(
        Guid Id,
        Guid CompanyId,
        Guid? DepartmentId,
        string Title,
        string? Description,
        bool IsActive,
        DateTimeOffset CreatedAt);

    private sealed record UpdatedPositionProfilePayload(
        Guid Id,
        Guid CompanyId,
        Guid? DepartmentId,
        string Title,
        string? Description,
        bool IsActive,
        DateTimeOffset UpdatedAt);
}
