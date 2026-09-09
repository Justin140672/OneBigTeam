using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// Ticket 2 (optimistic concurrency rollout): PublicHoliday.version coverage for
// PUT /api/companies/{companyId}/public-holidays/{id}, against the real Postgres-backed
// ApiWebApplicationFactory. Follows PublicHolidayEndpointTests for seeding/auth. The pre-edit
// Version is read from GET /api/companies/{companyId}/public-holidays (ListPublicHolidays carries it).
[Collection("Integration")]
public class UpdatePublicHolidayConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("ee000009-0000-0000-0000-000000000022");

    public UpdatePublicHolidayConcurrencyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Put_PublicHoliday_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/public-holidays/{Guid.NewGuid()}",
            new { date = "2026-12-26", name = "Boxing Day", countryCode = "GB" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_PublicHoliday_With_Correct_ExpectedVersion_Succeeds_And_Increments_Version()
    {
        var (client, companyId, id) = await SeedAsync();
        var version = (await GetVersionAsync(client, companyId, id));

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/public-holidays/{id}", Body(companyId, id, name: "Boxing Day", date: "2026-12-26", expectedVersion: version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = (await response.Content.ReadFromJsonAsync<HolidayPayload>())!;
        Assert.Equal(version + 1, payload.Version);
        Assert.Equal("Boxing Day", payload.Name);
    }

    [Fact]
    public async Task Two_Editors_Second_Stale_Save_Returns_409_Concurrency_And_First_Values_Preserved()
    {
        var (client, companyId, id) = await SeedAsync();
        var version = (await GetVersionAsync(client, companyId, id));

        var editorA = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/public-holidays/{id}", Body(companyId, id, name: "EditorA", date: "2026-12-25", expectedVersion: version));
        Assert.Equal(HttpStatusCode.OK, editorA.StatusCode);
        Assert.Equal(version + 1, (await editorA.Content.ReadFromJsonAsync<HolidayPayload>())!.Version);

        var editorB = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/public-holidays/{id}", Body(companyId, id, name: "EditorB", date: "2026-12-25", expectedVersion: version));

        Assert.Equal(HttpStatusCode.Conflict, editorB.StatusCode);
        Assert.Equal("concurrency", (await editorB.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    [Fact]
    public async Task Put_PublicHoliday_Without_ExpectedVersion_Returns_422_And_Writes_Nothing()
    {
        var (client, companyId, id) = await SeedAsync();

        var r1 = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/public-holidays/{id}", Body(companyId, id, name: "NoVersion1", date: "2026-12-25", expectedVersion: null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r1.StatusCode);

        var response = await client.GetAsync($"/api/companies/{companyId}/public-holidays");
        response.EnsureSuccessStatusCode();
        var current = (await response.Content.ReadFromJsonAsync<ListPayload>())!.Items.Single(i => i.Id == id);
        Assert.Equal("Christmas Day", current.Name);
        Assert.Equal(1, current.Version);
    }

    [Fact]
    public async Task Put_PublicHoliday_Returns_NotFound_For_Missing_Id()
    {
        var (client, companyId, _) = await SeedAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/public-holidays/{Guid.NewGuid()}",
            Body(companyId, Guid.NewGuid(), name: "Ghost", date: "2026-12-25", expectedVersion: 1));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_PublicHoliday_Returns_BadRequest_For_Empty_Name()
    {
        var (client, companyId, id) = await SeedAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/public-holidays/{id}",
            new { companyId, id, date = "2026-12-25", name = "", countryCode = "GB" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private static object Body(Guid companyId, Guid id, string name, string date, int? expectedVersion)
        => new { companyId, id, date, name, countryCode = "GB", expectedVersion };

    private async Task<(HttpClient Client, Guid CompanyId, Guid Id)> SeedAsync()
    {
        var companyId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, UserId, SystemRoles.HrAdministrator, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/public-holidays",
            new { companyId, date = "2026-12-25", name = "Christmas Day", countryCode = "GB" });
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<HolidayPayload>())!;
        return (client, companyId, created.Id);
    }

    private static async Task<int> GetVersionAsync(HttpClient client, Guid companyId, Guid id)
    {
        var response = await client.GetAsync($"/api/companies/{companyId}/public-holidays");
        response.EnsureSuccessStatusCode();
        var list = (await response.Content.ReadFromJsonAsync<ListPayload>())!;
        return list.Items.Single(i => i.Id == id).Version;
    }

    private sealed record ErrorPayload(string? Error, string? Code);
    private sealed record HolidayPayload(Guid Id, Guid CompanyId, string Name, string CountryCode, int Version);
    private sealed record ListPayload(IReadOnlyList<HolidayPayload> Items);
}
