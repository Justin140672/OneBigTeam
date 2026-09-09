using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// Ticket 2 (optimistic concurrency rollout): Company.version coverage for PUT /api/companies/{id},
// against the real Postgres-backed ApiWebApplicationFactory. Follows UpdateCompanyEndpointTests for
// seeding/auth helpers. The pre-edit Version is read from GET /api/companies/{id}, which carries it.
// Every request body keeps a RegisteredOffice address so the UpdateCompanyValidator's
// "Registered Office address is required" rule is satisfied except in the dedicated validation test.
[Collection("Integration")]
public class UpdateCompanyConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("ee000009-0000-0000-0000-000000000021");

    public UpdateCompanyConcurrencyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.CompanyAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Put_Company_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}", new { name = "Acme" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Company_With_Correct_ExpectedVersion_Succeeds_And_Increments_Version()
    {
        var (client, companyId) = await SeedAsync();
        var version = (await GetAsync(client, companyId)).Version;

        var response = await client.PutAsJsonAsync($"/api/companies/{companyId}", Body("Updated Company", version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = (await response.Content.ReadFromJsonAsync<CompanyPayload>())!;
        Assert.Equal(version + 1, payload.Version);
        Assert.Equal("Updated Company", payload.Name);
    }

    [Fact]
    public async Task Two_Editors_Second_Stale_Save_Returns_409_Concurrency_And_First_Values_Preserved()
    {
        var (client, companyId) = await SeedAsync();
        var version = (await GetAsync(client, companyId)).Version;

        var editorA = await client.PutAsJsonAsync($"/api/companies/{companyId}", Body("EditorA", version));
        Assert.Equal(HttpStatusCode.OK, editorA.StatusCode);
        Assert.Equal(version + 1, (await editorA.Content.ReadFromJsonAsync<CompanyPayload>())!.Version);

        var editorB = await client.PutAsJsonAsync($"/api/companies/{companyId}", Body("EditorB", version));

        Assert.Equal(HttpStatusCode.Conflict, editorB.StatusCode);
        Assert.Equal("concurrency", (await editorB.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);

        Assert.Equal("EditorA", (await GetAsync(client, companyId)).Name);
    }

    [Fact]
    public async Task Put_Company_Without_ExpectedVersion_Returns_422_And_Writes_Nothing()
    {
        var (client, companyId) = await SeedAsync();

        var before = await GetAsync(client, companyId);

        var r1 = await client.PutAsJsonAsync($"/api/companies/{companyId}", Body("NoVersion1", expectedVersion: null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r1.StatusCode);

        var after = await GetAsync(client, companyId);
        Assert.Equal(before.Name, after.Name);
    }

    [Fact]
    public async Task Put_Company_Returns_BadRequest_When_RegisteredOffice_Address_Missing()
    {
        var (client, companyId) = await SeedAsync();

        var response = await client.PutAsJsonAsync($"/api/companies/{companyId}", new
        {
            name = "Missing Address",
            addresses = new[]
            {
                new { type = "TradingAddress", line1 = "11 Billing Street", city = "Manchester", postalCode = (string?)null, countryCode = "GB" }
            }
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_Company_Returns_Forbidden_For_Tenant_With_No_Company()
    {
        // Under SEC-001 tenant isolation the route companyId must equal the caller's resolved
        // tenant, and CustomerSubscription has a hard FK to Company — so a "own tenant but company
        // row missing" 404 is unreachable for this mutation. Syncing the caller to a fresh tenant
        // with no seeded company/subscription surfaces as ReadOnlyModeMiddleware's 403 first.
        var tenantId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, UserId, SystemRoles.CompanyAdministrator, tenantId, ensureActiveSubscription: false);

        var response = await client.PutAsJsonAsync($"/api/companies/{tenantId}", Body("Unknown", expectedVersion: null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static object Body(string name, int? expectedVersion)
        => new
        {
            name,
            expectedVersion,
            addresses = new[]
            {
                new { type = "RegisteredOffice", line1 = "10 High Street", city = "London", postalCode = (string?)"SW1A 1AA", countryCode = "GB" },
                new { type = "TradingAddress", line1 = "11 Billing Street", city = "Manchester", postalCode = (string?)null, countryCode = "GB" }
            }
        };

    private async Task<(HttpClient Client, Guid CompanyId)> SeedAsync()
    {
        var tenantId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, UserId, SystemRoles.CompanyAdministrator, tenantId);

        var companyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Concurrency Test {Guid.NewGuid():N}", companyId: tenantId);
        return (client, companyId);
    }

    private static async Task<CompanyPayload> GetAsync(HttpClient client, Guid companyId)
    {
        var response = await client.GetAsync($"/api/companies/{companyId}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CompanyPayload>())!;
    }

    private sealed record ErrorPayload(string? Error, string? Code);
    private sealed record CompanyPayload(Guid Id, string Name, bool IsActive, int Version);
}
