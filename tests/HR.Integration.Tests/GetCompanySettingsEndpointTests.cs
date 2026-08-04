using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetCompanySettingsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("eeeeeeee-0000-0000-0000-000000000008");

    public GetCompanySettingsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AuthenticatedClient(Guid tenantId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, UserId, SystemRoles.Employee, tenantId);
        return client;
    }

    [Fact]
    public async Task Get_Company_Settings_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Company_Settings_Returns_Slimmed_ProfileScoped_Fields_When_Never_Customised()
    {
        using var client = await AuthenticatedClient(UserId);

        var createResponse = await client.PostAsJsonAsync("/api/companies", new
        {
            name = $"Settings Test {Guid.NewGuid():N}",
            addresses = new[]
            {
                new { type = "RegisteredOffice", line1 = "10 High Street", city = "London", countryCode = "GB" }
            }
        });
        createResponse.EnsureSuccessStatusCode();

        var createdCompany = await createResponse.Content.ReadFromJsonAsync<CreateCompanyPayload>();
        Assert.NotNull(createdCompany);

        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, createdCompany!.Id.ToString());

        var response = await client.GetAsync($"/api/companies/{createdCompany.Id}/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rawJson = await response.Content.ReadAsStringAsync();

        // Lock in that HR-policy fields no longer appear in the company-settings response.
        Assert.DoesNotContain("workingDays", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("employeeNumberMode", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("noticePeriodUnit", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("defaultAcknowledgementStatement", rawJson, StringComparison.OrdinalIgnoreCase);

        var payload = await response.Content.ReadFromJsonAsync<GetCompanySettingsPayload>();
        Assert.NotNull(payload);
        Assert.Equal(createdCompany.Id, payload!.CompanyId);
        Assert.Equal("UTC", payload.TimeZone);
        Assert.Equal("en-GB", payload.Locale);
        Assert.False(string.IsNullOrEmpty(payload.PostcodeRegex));
        Assert.False(string.IsNullOrEmpty(payload.TelephoneRegex));
        Assert.False(string.IsNullOrEmpty(payload.MobileRegex));
    }

    [Fact]
    public async Task Get_Company_Settings_Returns_NotFound_For_Unknown_Id()
    {
        using var client = await AuthenticatedClient(UserId);

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/settings");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record CreateCompanyPayload(Guid Id);

    private sealed record GetCompanySettingsPayload(
        Guid CompanyId,
        string TimeZone,
        string Locale,
        string PostcodeRegex,
        string TelephoneRegex,
        string MobileRegex,
        DateTimeOffset UpdatedAt);
}
