using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Domain;

namespace HR.Integration.Tests;

public class UpdateCompanyProfileEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public UpdateCompanyProfileEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Put_Company_Profile_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}/profile", new
        {
            name = "Acme"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Company_Profile_Updates_Name_And_Addresses()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "user-5");

        var createResponse = await client.PostAsJsonAsync("/api/companies", new
        {
            name = $"Update Test {Guid.NewGuid():N}",
            addresses = new[]
            {
                new
                {
                    type = CompanyAddressType.RegisteredOffice,
                    line1 = "10 High Street",
                    city = "London",
                    countryCode = "GB"
                }
            }
        });
        createResponse.EnsureSuccessStatusCode();

        var createdCompany = await createResponse.Content.ReadFromJsonAsync<CreateCompanyPayload>();
        Assert.NotNull(createdCompany);

        var response = await client.PutAsJsonAsync($"/api/companies/{createdCompany!.Id}/profile", new
        {
            name = "Updated Company",
            addresses = new[]
            {
                new
                {
                    type = CompanyAddressType.RegisteredOffice,
                    line1 = "10 High Street",
                    city = "London",
                    postalCode = (string?)"SW1A 1AA",
                    countryCode = "GB"
                },
                new
                {
                    type = CompanyAddressType.TradingAddress,
                    line1 = "11 Billing Street",
                    city = "Manchester",
                    postalCode = (string?)null,
                    countryCode = "GB"
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<UpdateCompanyProfilePayload>();
        Assert.NotNull(payload);
        Assert.Equal("Updated Company", payload!.Name);
        Assert.NotNull(payload.Branding);
        Assert.Equal("#0055AA", payload.Branding.PrimaryColor);
        Assert.Equal(2, payload.Addresses.Count);
        Assert.Contains(payload.Addresses, address => address.Type == CompanyAddressType.RegisteredOffice && address.City == "London");
        Assert.Contains(payload.Addresses, address => address.Type == CompanyAddressType.TradingAddress && address.City == "Manchester");
    }

    [Fact]
    public async Task Put_Company_Profile_Returns_NotFound_For_Unknown_Id()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "user-6");

        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}/profile", new
        {
            name = "Unknown",
            addresses = new[]
            {
                new
                {
                    type = CompanyAddressType.RegisteredOffice,
                    line1 = "10 High Street",
                    city = "London",
                    countryCode = "GB"
                }
            }
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
        DateTimeOffset UpdatedAt,
        CompanyBrandingPayload Branding,
        IReadOnlyCollection<CompanyAddressPayload> Addresses);

    private sealed record CompanyBrandingPayload(
        string? PrimaryLogoUrl,
        string? SmallLogoUrl,
        string? EmailLogoUrl,
        string PrimaryColor,
        string SecondaryColor,
        string AccentColor,
        DateTimeOffset UpdatedAt);

    private sealed record CompanyAddressPayload(
        Guid Id,
        CompanyAddressType Type,
        string Line1,
        string? Line2,
        string City,
        string? Region,
        string? PostalCode,
        string CountryCode);
}
