using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Domain;

namespace HR.Integration.Tests;

public class CreateCompanyEndpointTests : IClassFixture<ApiWebApplicationFactory>
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
            name = "Acme Corp",
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

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Companies_Creates_Company_For_Authenticated_Request()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "user-1");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, "tenant-1");

        var response = await client.PostAsJsonAsync("/api/companies", new
        {
            name = "New Corp",
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

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<CreateCompanyPayload>();
        Assert.NotNull(payload);
        Assert.Equal("New Corp", payload!.Name);
        Assert.Equal("new-corp", payload.Slug);
        Assert.True(payload.IsActive);
        Assert.NotEqual(Guid.Empty, payload.Id);
        Assert.Equal(2, payload.Addresses.Count);
        Assert.Contains(payload.Addresses, address => address.Type == CompanyAddressType.RegisteredOffice);
        Assert.Contains(payload.Addresses, address => address.Type == CompanyAddressType.TradingAddress);
    }

    [Fact]
    public async Task Post_Companies_Returns_Conflict_For_Duplicate_Slug()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "user-2");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, "tenant-2");

        var firstResponse = await client.PostAsJsonAsync("/api/companies", new
        {
            name = "Globex",
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
        firstResponse.EnsureSuccessStatusCode();

        var secondResponse = await client.PostAsJsonAsync("/api/companies", new
        {
            name = "Globex",
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

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    private sealed record CreateCompanyPayload(
        Guid Id,
        string Name,
        string Slug,
        bool IsActive,
        DateTimeOffset CreatedAt,
        IReadOnlyCollection<CompanyAddressPayload> Addresses);

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
