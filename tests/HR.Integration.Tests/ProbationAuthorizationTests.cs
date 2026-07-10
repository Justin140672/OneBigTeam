using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// Proves the probation:manage / probation:review FastEndpoints policy declarations
/// actually enforce access end-to-end over real HTTP. Company Administrator is scoped to
/// company profile/settings management only and no longer holds either permission — see
/// the narrowing in HR.Modules.Identity.IdentityModule.AddRolePolicies.
/// </summary>
public class ProbationAuthorizationTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid CompanyAdministratorUser = new("ff000001-0000-0000-0000-000000000001");

    public ProbationAuthorizationTests(ApiWebApplicationFactory factory)
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

    // --- probation:manage — CreateProbationRecord ---

    [Fact]
    public async Task CompanyAdministrator_Gets_Forbidden_Creating_Probation_Record()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientFor(companyId, CompanyAdministratorUser);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-records", new
        {
            companyId,
            employeeId = Guid.NewGuid(),
            managerEmployeeId = Guid.NewGuid(),
            startDate = "2026-07-01",
            expectedEndDate = "2026-10-01"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- probation:review — GetProbationReview ---

    [Fact]
    public async Task CompanyAdministrator_Gets_Forbidden_Getting_Probation_Review()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientFor(companyId, CompanyAdministratorUser);

        var response = await client.GetAsync($"/api/companies/{companyId}/probation-reviews/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
