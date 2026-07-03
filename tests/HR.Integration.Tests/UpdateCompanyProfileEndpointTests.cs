using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class UpdateCompanyEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("eeeeeeee-0000-0000-0000-000000000006");

    public UpdateCompanyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    private HttpClient AuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, UserId.ToString());
        return client;
    }

    [Fact]
    public async Task Put_Company_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}", new
        {
            name = "Acme"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Company_Updates_Name_And_Addresses()
    {
        using var client = AuthenticatedClient();

        var createResponse = await client.PostAsJsonAsync("/api/companies", new
        {
            name = $"Update Test {Guid.NewGuid():N}",
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

        var response = await client.PutAsJsonAsync($"/api/companies/{createdCompany.Id}", new
        {
            name = "Updated Company",
            addresses = new[]
            {
                new { type = "RegisteredOffice", line1 = "10 High Street", city = "London", postalCode = (string?)"SW1A 1AA", countryCode = "GB" },
                new { type = "TradingAddress", line1 = "11 Billing Street", city = "Manchester", postalCode = (string?)null, countryCode = "GB" }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<UpdateCompanyPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Updated Company", payload!.Name);
        Assert.Equal(2, payload.Addresses.Count);
        Assert.Contains(payload.Addresses, a => a.Type == "RegisteredOffice" && a.City == "London");
        Assert.Contains(payload.Addresses, a => a.Type == "TradingAddress" && a.City == "Manchester");
    }

    [Fact]
    public async Task Put_Company_Returns_NotFound_For_Unknown_Id()
    {
        using var client = AuthenticatedClient();

        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}", new
        {
            name = "Unknown",
            addresses = new[]
            {
                new { type = "RegisteredOffice", line1 = "10 High Street", city = "London", countryCode = "GB" }
            }
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record CreateCompanyPayload(Guid Id, string Name);

    private sealed record UpdateCompanyPayload(
        Guid Id,
        string Name,
        bool IsActive,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        IReadOnlyCollection<CompanyAddressPayload> Addresses);

    private sealed record CompanyAddressPayload(
        Guid Id,
        string Type,
        string Line1,
        string? Line2,
        string City,
        string? Region,
        string? PostalCode,
        string CountryCode);
}
