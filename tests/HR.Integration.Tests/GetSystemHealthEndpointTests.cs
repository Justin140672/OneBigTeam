using System.Net;
using System.Net.Http.Json;

using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetSystemHealthEndpointTests
{
    private const string AllowListedEmail = "priya.shah@acme.example";
    private const string Url = "/api/companies/admin/system-health";

    private readonly ApiWebApplicationFactory _factory;

    public GetSystemHealthEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientFor(Guid userId, string? email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        if (!string.IsNullOrWhiteSpace(email))
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, email);
        }

        return client;
    }

    [Fact]
    public async Task Get_SystemHealth_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_SystemHealth_Returns_Unauthorized_For_Authenticated_Caller_Not_On_AllowList()
    {
        using var client = ClientFor(Guid.NewGuid(), "not-allow-listed@example.com");

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_SystemHealth_Returns_Ok_With_All_Six_Categories_For_AllowListed_Caller()
    {
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.GetAsync(Url);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<SystemHealthPayload>();
        Assert.NotNull(payload);
        Assert.False(string.IsNullOrWhiteSpace(payload!.OverallStatus));
        Assert.False(string.IsNullOrWhiteSpace(payload.PlatformVersion));
        Assert.True(payload.CheckedAt > DateTimeOffset.UtcNow.AddMinutes(-5));

        var expectedCategoryNames = new[] { "Database", "Storage", "Authentication", "Email", "Stripe", "Background Jobs" };
        Assert.Equal(expectedCategoryNames.Length, payload.Categories.Count);

        foreach (var expectedName in expectedCategoryNames)
        {
            var category = payload.Categories.SingleOrDefault(c => c.Name == expectedName);
            Assert.NotNull(category);
            Assert.False(string.IsNullOrWhiteSpace(category!.Status));
        }
    }

    private sealed record SystemHealthPayload(
        string OverallStatus,
        string PlatformVersion,
        DateTimeOffset CheckedAt,
        IReadOnlyList<SystemHealthCategoryPayload> Categories);

    private sealed record SystemHealthCategoryPayload(string Name, string Status, string? Description);
}
