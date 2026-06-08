using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Domain;

namespace HR.Integration.Tests;

public class UpdateCompanyEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public UpdateCompanyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
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
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "user-5");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, "tenant-5");

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

        var response = await client.PutAsJsonAsync($"/api/companies/{createdCompany!.Id}", new
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

        var payload = await response.Content.ReadFromJsonAsync<UpdateCompanyPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Updated Company", payload!.Name);
        Assert.Equal(2, payload.Addresses.Count);
        Assert.Contains(payload.Addresses, a => a.Type == CompanyAddressType.RegisteredOffice && a.City == "London");
        Assert.Contains(payload.Addresses, a => a.Type == CompanyAddressType.TradingAddress && a.City == "Manchester");
        Assert.Null(payload.Branding);
    }

    [Fact]
    public async Task Put_Company_Updates_Branding_Colors_When_Provided()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "user-9");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, "tenant-9");

        var createResponse = await client.PostAsJsonAsync("/api/companies", new
        {
            name = $"Branding Test {Guid.NewGuid():N}",
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

        var response = await client.PutAsJsonAsync($"/api/companies/{createdCompany!.Id}", new
        {
            name = createdCompany.Name,
            addresses = new[]
            {
                new
                {
                    type = CompanyAddressType.RegisteredOffice,
                    line1 = "10 High Street",
                    city = "London",
                    countryCode = "GB"
                }
            },
            branding = new
            {
                primaryColor = "#FF5733",
                secondaryColor = "#C70039",
                accentColor = "#900C3F"
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<UpdateCompanyPayload>();
        Assert.NotNull(payload);
        Assert.NotNull(payload!.Branding);
        Assert.Equal("#FF5733", payload.Branding!.PrimaryColor);
        Assert.Equal("#C70039", payload.Branding.SecondaryColor);
        Assert.Equal("#900C3F", payload.Branding.AccentColor);
    }

    [Fact]
    public async Task Put_Company_Returns_NotFound_For_Unknown_Id()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "user-6");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, "tenant-6");

        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}", new
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

    private sealed record CreateCompanyPayload(Guid Id, string Name);

    private sealed record UpdateCompanyPayload(
        Guid Id,
        string Name,
        string Slug,
        bool IsActive,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        IReadOnlyCollection<CompanyAddressPayload> Addresses,
        BrandingPayload? Branding);

    private sealed record CompanyAddressPayload(
        Guid Id,
        CompanyAddressType Type,
        string Line1,
        string? Line2,
        string City,
        string? Region,
        string? PostalCode,
        string CountryCode);

    private sealed record BrandingPayload(
        string PrimaryColor,
        string SecondaryColor,
        string AccentColor);
}
