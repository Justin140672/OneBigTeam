using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UpdateCompanySettingsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("eeeeeeee-0000-0000-0000-000000000007");

    public UpdateCompanySettingsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.CompanyAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AuthenticatedClient(Guid tenantId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, UserId, SystemRoles.CompanyAdministrator, tenantId);
        return client;
    }

    [Fact]
    public async Task Put_Company_Settings_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}/settings", new
        {
            timeZone = "UTC",
            locale = "en-GB",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Company_Settings_Updates_Settings_For_Authenticated_Request()
    {
        using var client = await AuthenticatedClient(UserId);

        // POST /api/companies (CreateCompany) was removed in 78a43344; seed the company directly
        // via CompaniesDbContext instead, mirroring TestRoleSeeder.EnsureActiveSubscriptionAsync.
        var createdCompanyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Settings Test {Guid.NewGuid():N}");

        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, createdCompanyId.ToString());

        var response = await client.PutAsJsonAsync($"/api/companies/{createdCompanyId}/settings", new
        {
            timeZone = "Europe/London",
            locale = "en-GB",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rawJson = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("workingDays", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("employeeNumberMode", rawJson, StringComparison.OrdinalIgnoreCase);

        var payload = await response.Content.ReadFromJsonAsync<UpdateCompanySettingsPayload>();
        Assert.NotNull(payload);
        Assert.Equal(createdCompanyId, payload!.CompanyId);
        Assert.Equal("Europe/London", payload.TimeZone);
        Assert.Equal("en-GB", payload.Locale);
    }

    [Fact]
    public async Task Put_Company_Settings_Returns_UnprocessableEntity_When_TimeZone_Is_Blank()
    {
        using var client = await AuthenticatedClient(UserId);

        // POST /api/companies (CreateCompany) was removed in 78a43344; seed the company directly
        // via CompaniesDbContext instead, mirroring TestRoleSeeder.EnsureActiveSubscriptionAsync.
        var createdCompanyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Settings Test {Guid.NewGuid():N}");

        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, createdCompanyId.ToString());

        var response = await client.PutAsJsonAsync($"/api/companies/{createdCompanyId}/settings", new
        {
            timeZone = string.Empty,
            locale = "en-GB",
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_Company_Settings_Returns_NotFound_For_Unknown_Id()
    {
        using var client = await AuthenticatedClient(UserId);

        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}/settings", new
        {
            timeZone = "UTC",
            locale = "en-GB",
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record UpdateCompanySettingsPayload(
        Guid CompanyId,
        string TimeZone,
        string Locale,
        DateTimeOffset UpdatedAt);
}
