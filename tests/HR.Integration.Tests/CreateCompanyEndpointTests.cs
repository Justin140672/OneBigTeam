using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

[Collection(ApiIntegrationCollection.Name)]
public class CreateCompanyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public CreateCompanyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Post_Companies_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/companies", new
        {
            name = "Acme Corp"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Companies_Creates_Company_For_Authenticated_Request()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "user-1");

        var response = await client.PostAsJsonAsync("/api/companies", new
        {
            name = "Acme Corp"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<CreateCompanyPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Acme Corp", payload!.Name);
        Assert.Equal("acme-corp", payload.Slug);
        Assert.True(payload.IsActive);
        Assert.NotEqual(Guid.Empty, payload.Id);
    }

    [Fact]
    public async Task Post_Companies_Returns_Conflict_For_Duplicate_Slug()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "user-2");

        var firstResponse = await client.PostAsJsonAsync("/api/companies", new
        {
            name = "Globex"
        });
        firstResponse.EnsureSuccessStatusCode();

        var secondResponse = await client.PostAsJsonAsync("/api/companies", new
        {
            name = "Globex"
        });

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    private sealed record CreateCompanyPayload(
        Guid Id,
        string Name,
        string Slug,
        bool IsActive,
        DateTimeOffset CreatedAt);
}
