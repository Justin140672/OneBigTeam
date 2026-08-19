using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UpdateCompanyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("eeeeeeee-0000-0000-0000-000000000006");

    public UpdateCompanyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.CompanyAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AuthenticatedClient(Guid tenantId, bool ensureActiveSubscription = true)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, UserId, SystemRoles.CompanyAdministrator, tenantId, ensureActiveSubscription);
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
        // The route companyId must match the caller's resolved tenant (UserProfile.CompanyId),
        // which TenantRouteAuthorizationMiddleware now enforces — seed the company under the
        // same fresh tenant id the caller is synced to, rather than an unrelated random id.
        var tenantId = Guid.NewGuid();
        using var client = await AuthenticatedClient(tenantId);

        // POST /api/companies (CreateCompany) was removed in 78a43344; seed the company directly
        // via CompaniesDbContext instead, mirroring TestRoleSeeder.EnsureActiveSubscriptionAsync.
        var createdCompanyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Update Test {Guid.NewGuid():N}", companyId: tenantId);

        var response = await client.PutAsJsonAsync($"/api/companies/{createdCompanyId}", new
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
    public async Task Put_Company_Returns_Forbidden_For_Unknown_Id()
    {
        // Under the SEC-001 tenant-isolation fix, a route companyId must match the caller's
        // resolved tenant, and CustomerSubscription has a hard FK to Company — so a subscription
        // can never exist without a real Company row for the same id. There is therefore no
        // reachable "own tenant, but company row is unexpectedly missing" 404 case for this
        // mutation endpoint any more: syncing the caller to a fresh tenant id with no seeded
        // Company/subscription now surfaces as ReadOnlyModeMiddleware's missing-subscription 403
        // before the handler's own lookup would ever run.
        var tenantId = Guid.NewGuid();
        using var client = await AuthenticatedClient(tenantId, ensureActiveSubscription: false);

        var response = await client.PutAsJsonAsync($"/api/companies/{tenantId}", new
        {
            name = "Unknown",
            addresses = new[]
            {
                new { type = "RegisteredOffice", line1 = "10 High Street", city = "London", countryCode = "GB" }
            }
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

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
