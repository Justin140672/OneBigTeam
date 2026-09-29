using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ProbationAuthorizationTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid CompanyAdministratorUser = new("ff000001-0000-0000-0000-000000000001");
    private static readonly Guid ManagerUser = new("ff000001-0000-0000-0000-000000000002");
    private static readonly Guid PlainEmployeeUser = new("ff000001-0000-0000-0000-000000000003");

    public ProbationAuthorizationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdministratorUser, SystemRoles.CompanyAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ManagerUser, SystemRoles.Manager);
            await TestRoleSeeder.AssignRoleAsync(factory, PlainEmployeeUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientFor(Guid companyId, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }


    [Fact]
    public async Task CompanyAdministrator_Gets_Forbidden_Creating_Probation_Record()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, CompanyAdministratorUser);

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


    [Fact]
    public async Task CompanyAdministrator_Gets_Forbidden_Getting_Probation_Review()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, CompanyAdministratorUser);

        var response = await client.GetAsync($"/api/companies/{companyId}/probation-reviews/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }


    [Fact]
    public async Task Manager_Gets_Ok_Getting_Upcoming_Probation_Reviews()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, ManagerUser);

        var response = await client.GetAsync($"/api/companies/{companyId}/probation-reviews/upcoming");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PlainEmployee_Gets_Forbidden_Getting_Upcoming_Probation_Reviews()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, PlainEmployeeUser);

        var response = await client.GetAsync($"/api/companies/{companyId}/probation-reviews/upcoming");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
