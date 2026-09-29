using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.SharedKernel;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetCompanyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AuthenticatedUser = new("ee000002-0000-0000-0000-000000000001");

    public GetCompanyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        TestRoleSeeder.AssignRoleAsync(factory, AuthenticatedUser, SystemRoles.Employee)
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Get_Company_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Company_Returns_Company_For_Authenticated_Request()
    {
        var tenantId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AuthenticatedUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());

        var companyName = $"Get Test {Guid.NewGuid():N}";
        var createdCompanyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, companyName, companyId: tenantId);
        await TestRoleSeeder.SyncCompanyAsync(_factory, AuthenticatedUser, tenantId);

        var response = await client.GetAsync($"/api/companies/{createdCompanyId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<GetCompanyPayload>();
        Assert.NotNull(payload);
        Assert.Equal(createdCompanyId, payload!.Id);
        Assert.Equal(companyName, payload.Name);
        Assert.True(payload.IsActive);
        Assert.Empty(payload.Addresses);
    }

    [Fact]
    public async Task Get_Company_Returns_NotFound_For_Unknown_Id()
    {
        var tenantId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AuthenticatedUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, AuthenticatedUser, tenantId, ensureActiveSubscription: false);

        var response = await client.GetAsync($"/api/companies/{tenantId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record GetCompanyPayload(
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
