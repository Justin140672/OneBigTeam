using System.Net;
using System.Text.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ListInternalVacanciesEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid EmployeeUser = new("cc00001f-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public ListInternalVacanciesEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, EmployeeUser, SystemRoles.Employee))
            .GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, EmployeeUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, EmployeeUser, SystemRoles.Employee, companyId);
        return client;
    }

    private static Vacancy OpenAdvertised(Guid companyId, string advertTitle)
    {
        var v = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), advertTitle, null, Guid.NewGuid(), Now,
            assignedRecruiterId: null, isAdvertisedInternally: true);
        v.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        return v;
    }

    [Fact]
    public async Task Get_InternalVacancies_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/internal-vacancies");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_InternalVacancies_Returns_Forbidden_When_Tenant_Claim_Does_Not_Match_Route()
    {
        var companyId = Guid.NewGuid();
        var differentCompany = Guid.NewGuid();
        using var client = await AuthenticatedClient(differentCompany);

        var response = await client.GetAsync($"/api/companies/{companyId}/internal-vacancies");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_InternalVacancies_Returns_Only_Open_Advertised_SameCompany_Vacancies()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var openAdvertised = OpenAdvertised(companyId, "Open Advertised Role");

        var openNotAdvertised = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Open Not Advertised", null, Guid.NewGuid(), Now);
        openNotAdvertised.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));

        var draftAdvertised = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Draft Advertised", null, Guid.NewGuid(), Now,
            assignedRecruiterId: null, isAdvertisedInternally: true);

        var closedAdvertised = OpenAdvertised(companyId, "Closed Advertised");
        closedAdvertised.Close(Now, DateOnly.FromDateTime(Now.UtcDateTime));

        var otherCompanyOpenAdvertised = OpenAdvertised(otherCompanyId, "Other Company Role");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            db.Vacancies.AddRange(openAdvertised, openNotAdvertised, draftAdvertised, closedAdvertised, otherCompanyOpenAdvertised);
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/internal-vacancies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(openAdvertised.Id, items[0].GetProperty("id").GetGuid());
        Assert.Equal("Open Advertised Role", items[0].GetProperty("title").GetString());
    }
}
