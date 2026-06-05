using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

public class GetCompanyEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public GetCompanyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_Company_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Company_Returns_Company_For_Authenticated_Request()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "user-3");

        var createResponse = await client.PostAsJsonAsync("/api/companies", new
        {
            name = $"Get Test {Guid.NewGuid():N}"
        });
        createResponse.EnsureSuccessStatusCode();

        var createdCompany = await createResponse.Content.ReadFromJsonAsync<CreateCompanyPayload>();
        Assert.NotNull(createdCompany);

        var response = await client.GetAsync($"/api/companies/{createdCompany!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<GetCompanyPayload>();
        Assert.NotNull(payload);
        Assert.Equal(createdCompany.Id, payload!.Id);
        Assert.Equal(createdCompany.Name, payload.Name);
        Assert.Equal(createdCompany.Slug, payload.Slug);
        Assert.Equal(createdCompany.IsActive, payload.IsActive);
    }

    [Fact]
    public async Task Get_Company_Returns_NotFound_For_Unknown_Id()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "user-4");

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record CreateCompanyPayload(
        Guid Id,
        string Name,
        string Slug,
        bool IsActive,
        DateTimeOffset CreatedAt);

    private sealed record GetCompanyPayload(
        Guid Id,
        string Name,
        string Slug,
        bool IsActive,
        DateTimeOffset CreatedAt);
}