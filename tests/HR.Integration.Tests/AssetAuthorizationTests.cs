using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// Proves the asset:view / employee:manage FastEndpoints policy declarations actually
/// enforce access end-to-end over real HTTP for the Assets module. Company Administrator
/// is scoped to company profile/settings management only and no longer holds either
/// permission — see the narrowing in HR.Modules.Identity.IdentityModule.AddRolePolicies.
/// </summary>
[Collection("Integration")]
public class AssetAuthorizationTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid CompanyAdministratorUser = new("11000002-0000-0000-0000-000000000001");

    public AssetAuthorizationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdministratorUser, SystemRoles.CompanyAdministrator);
        }).GetAwaiter().GetResult();
    }

    private HttpClient ClientFor(Guid companyId, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    // --- asset:view — ListEmployeeAssets ---

    [Fact]
    public async Task CompanyAdministrator_Gets_Forbidden_Listing_Employee_Assets()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientFor(companyId, CompanyAdministratorUser);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/assets");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- employee:manage — CreateAssetCategory ---

    [Fact]
    public async Task CompanyAdministrator_Gets_Forbidden_Creating_Asset_Category()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientFor(companyId, CompanyAdministratorUser);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/asset-categories", new
        {
            companyId,
            name = "Laptops",
            description = "Company laptops"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
