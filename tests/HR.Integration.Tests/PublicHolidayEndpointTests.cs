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

    [Fact]
    public async Task Put_PublicHoliday_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{CompanyId}/public-holidays/{Guid.NewGuid()}",
            new { date = "2026-12-26", name = "Boxing Day", countryCode = "GB" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_PublicHoliday_Returns_Ok_With_Updated_Values()
    {
        using var client = AuthenticatedClient();
        var companyId = Guid.NewGuid();

        var createResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/public-holidays",
            new { companyId, date = "2026-12-25", name = "Christmas Day", countryCode = "GB" });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<PublicHolidayPayload>();

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/public-holidays/{created!.Id}",
            new { companyId, id = created.Id, date = "2026-12-26", name = "Boxing Day", countryCode = "GB" });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<PublicHolidayPayload>();
        Assert.Equal("Boxing Day", updated!.Name);
    }

    [Fact]
    public async Task Put_PublicHoliday_Returns_NotFound_When_Not_Exist()
    {
        using var client = AuthenticatedClient();
        var companyId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/public-holidays/{Guid.NewGuid()}",
            new { companyId, id = Guid.NewGuid(), date = "2026-12-25", name = "Christmas Day", countryCode = "GB" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_PublicHoliday_Returns_Conflict_When_Date_Already_Taken()
    {
        using var client = AuthenticatedClient();
        var companyId = Guid.NewGuid();

        var r1 = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/public-holidays",
            new { companyId, date = "2026-12-25", name = "Christmas Day", countryCode = "GB" });
        r1.EnsureSuccessStatusCode();
        var h1 = await r1.Content.ReadFromJsonAsync<PublicHolidayPayload>();

        var r2 = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/public-holidays",
            new { companyId, date = "2026-12-26", name = "Boxing Day", countryCode = "GB" });
        r2.EnsureSuccessStatusCode();

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/public-holidays/{h1!.Id}",
            new { companyId, id = h1.Id, date = "2026-12-26", name = "Christmas Day", countryCode = "GB" });

        Assert.Equal(HttpStatusCode.Conflict, updateResponse.StatusCode);
    }

    private sealed record PublicHolidayPayload(Guid Id, Guid CompanyId, string Name, string CountryCode);
    private sealed record ListPayload(IReadOnlyList<PublicHolidayPayload> Items);
}
