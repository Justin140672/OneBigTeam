using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

public class PublicHolidayEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly string UserId = Guid.NewGuid().ToString();

    public PublicHolidayEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient AuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, CompanyId.ToString());
        return client;
    }

    [Fact]
    public async Task Post_PublicHoliday_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{CompanyId}/public-holidays",
            new { date = "2026-12-25", name = "Christmas Day", countryCode = "GB" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_PublicHoliday_Returns_Created_With_Location_Header()
    {
        using var client = AuthenticatedClient();
        var companyId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/public-holidays",
            new { companyId, date = "2026-12-25", name = "Christmas Day", countryCode = "GB" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var payload = await response.Content.ReadFromJsonAsync<PublicHolidayPayload>();
        Assert.NotNull(payload);
        Assert.Equal(companyId, payload!.CompanyId);
        Assert.Equal("Christmas Day", payload.Name);
        Assert.Equal("GB", payload.CountryCode);
        Assert.NotEqual(Guid.Empty, payload.Id);
    }

    [Fact]
    public async Task Post_PublicHoliday_Returns_Conflict_For_Duplicate_Date()
    {
        using var client = AuthenticatedClient();
        var companyId = Guid.NewGuid();

        var first = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/public-holidays",
            new { companyId, date = "2026-01-01", name = "New Year's Day", countryCode = "GB" });
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/public-holidays",
            new { companyId, date = "2026-01-01", name = "New Year's Day", countryCode = "GB" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Get_PublicHolidays_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{CompanyId}/public-holidays?year=2026");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_PublicHolidays_Returns_List_For_Year()
    {
        using var client = AuthenticatedClient();
        var companyId = Guid.NewGuid();

        await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/public-holidays",
            new { companyId, date = "2026-03-17", name = "St Patrick's Day", countryCode = "IE" });
        await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/public-holidays",
            new { companyId, date = "2026-06-01", name = "June Bank Holiday", countryCode = "IE" });

        var response = await client.GetAsync($"/api/companies/{companyId}/public-holidays?year=2026");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Items.Count);
    }

private sealed record PublicHolidayPayload(Guid Id, Guid CompanyId, string Name, string CountryCode);
    private sealed record ListPayload(IReadOnlyList<PublicHolidayPayload> Items);
}
