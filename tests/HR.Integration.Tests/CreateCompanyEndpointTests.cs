using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.SharedKernel;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class CreateCompanyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AuthenticatedUser = new("dd000001-0000-0000-0000-000000000001");

    public CreateCompanyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        TestRoleSeeder.AssignRoleAsync(factory, AuthenticatedUser, SystemRoles.Employee)
            .GetAwaiter().GetResult();
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
                    type = "RegisteredOffice",
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
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AuthenticatedUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Guid.NewGuid().ToString());

        var response = await client.PostAsJsonAsync("/api/companies", new
        {
            name = "New Corp",
            addresses = new[]
            {
                new
                {
                    type = "RegisteredOffice",
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
        Assert.True(payload.IsActive);
        Assert.NotEqual(Guid.Empty, payload.Id);
        Assert.Equal(2, payload.Addresses.Count);
        Assert.Contains(payload.Addresses, address => address.Type == "RegisteredOffice");
        Assert.Contains(payload.Addresses, address => address.Type == "TradingAddress");
    }

    private sealed record CreateCompanyPayload(
        Guid Id,
        string Name,
        bool IsActive,
        DateTimeOffset CreatedAt,
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
