using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

[Collection(ApiIntegrationCollection.Name)]
public class UpdateCompanyProfileEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public UpdateCompanyProfileEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Put_Company_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}", new
        {
            name = "Updated Name"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Company_Updates_Profile_For_Authenticated_Request()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "user-5");

        var createResponse = await client.PostAsJsonAsync("/api/companies", new
        {
            name = $"Profile Update {Guid.NewGuid():N}"
        });
        createResponse.EnsureSuccessStatusCode();

        var createdCompany = await createResponse.Content.ReadFromJsonAsync<CreateCompanyPayload>();
        Assert.NotNull(createdCompany);

        var updateResponse = await client.PutAsJsonAsync($"/api/companies/{createdCompany!.Id}", new
        {
            name = "Renamed Company"
        });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var payload = await updateResponse.Content.ReadFromJsonAsync<UpdateCompanyProfilePayload>();
        Assert.NotNull(payload);
        Assert.Equal(createdCompany.Id, payload!.Id);
        Assert.Equal("Renamed Company", payload.Name);
        Assert.Equal(createdCompany.Slug, payload.Slug);
        Assert.Equal(createdCompany.IsActive, payload.IsActive);
        Assert.True(payload.UpdatedAt >= payload.CreatedAt);
    }

    [Fact]
    public async Task Put_Company_Returns_NotFound_For_Unknown_Id()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "user-6");

        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}", new
        {
            name = "Renamed Company"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record CreateCompanyPayload(
        Guid Id,
        string Name,
        string Slug,
        bool IsActive,
        DateTimeOffset CreatedAt);

    private sealed record UpdateCompanyProfilePayload(
        Guid Id,
        string Name,
        string Slug,
        bool IsActive,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}