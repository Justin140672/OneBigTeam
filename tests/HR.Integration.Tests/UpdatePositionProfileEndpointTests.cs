using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

public class UpdatePositionProfileEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public UpdatePositionProfileEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
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
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "upd-pp-user-1");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            title = "Original Title",
            isManagerial = false
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
                description = "Now with description",
                isManagerial = true
            });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var payload = await updateResponse.Content.ReadFromJsonAsync<UpdatedPositionProfilePayload>();
        Assert.NotNull(payload);
        Assert.Equal("Updated Title", payload!.Title);
        Assert.Equal("Now with description", payload.Description);
        Assert.True(payload.IsManagerial);
    }

    [Fact]
    public async Task Put_PositionProfile_Returns_NotFound_For_Unknown_Id()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "upd-pp-user-2");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{Guid.NewGuid()}", new
            {
                companyId,
                id = Guid.NewGuid(),
                title = "Whatever",
                isManagerial = false
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_PositionProfile_Returns_Conflict_For_Duplicate_Title()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "upd-pp-user-3");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var create1 = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            title = "Profile A",
            isManagerial = false
        });
        create1.EnsureSuccessStatusCode();

        var create2 = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            title = "Profile B",
            isManagerial = false
        });
        create2.EnsureSuccessStatusCode();
        var profileB = await create2.Content.ReadFromJsonAsync<PositionProfilePayload>();
        Assert.NotNull(profileB);

        // Try to rename Profile B to Profile A
        var updateResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{profileB!.Id}", new
            {
                companyId,
                id = profileB.Id,
                title = "Profile A",
                isManagerial = false
            });

        Assert.Equal(HttpStatusCode.Conflict, updateResponse.StatusCode);
    }

    [Fact]
    public async Task Put_PositionProfile_Returns_NotFound_When_Profile_Belongs_To_Different_Company()
    {
        using var client = _factory.CreateClient();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "upd-pp-user-4");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyA.ToString());

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyA}/position-profiles", new
        {
            companyId = companyA,
            title = "Designer",
            isManagerial = false
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<PositionProfilePayload>();
        Assert.NotNull(created);

        // Attempt update from company B's scope
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyB}/position-profiles/{created!.Id}", new
            {
                companyId = companyB,
                id = created.Id,
                title = "Designer",
                isManagerial = false
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record PositionProfilePayload(
        Guid Id,
        Guid CompanyId,
        Guid? DepartmentId,
        string Title,
        string? Description,
        bool IsManagerial,
        bool IsActive,
        DateTimeOffset CreatedAt);

    private sealed record UpdatedPositionProfilePayload(
        Guid Id,
        Guid CompanyId,
        Guid? DepartmentId,
        string Title,
        string? Description,
        bool IsManagerial,
        bool IsActive,
        DateTimeOffset UpdatedAt);
}
