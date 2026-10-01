using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class SupportManagementRoleBoundaryTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid CompanyAdminOnly = new("0b719900-0000-0000-0000-000000000001");
    private static readonly Guid CompanyAdminPlusHrAdmin = new("0b719900-0000-0000-0000-000000000002");

    public SupportManagementRoleBoundaryTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public enum TenantPersona
    {
        Employee,
        Manager,
        Recruiter,
        CompanyAdministratorOnly,
    }

    private static Guid[] RolesFor(TenantPersona persona) => persona switch
    {
        TenantPersona.Employee => [SystemRoles.Employee],
        TenantPersona.Manager => [SystemRoles.Employee, SystemRoles.Manager],
        TenantPersona.Recruiter => [SystemRoles.Employee, SystemRoles.Recruiter],
        TenantPersona.CompanyAdministratorOnly => [SystemRoles.Employee, SystemRoles.CompanyAdministrator],
        _ => throw new ArgumentOutOfRangeException(nameof(persona)),
    };

    private async Task<HttpClient> ClientFor(Guid userId, Guid companyId, bool alsoHrAdmin)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.CompanyAdministrator, companyId);
        if (alsoHrAdmin)
            await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, companyId);

        return client;
    }

    private async Task<HttpClient> ClientWithRoles(Guid companyId, params Guid[] roles)
    {
        var userId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        foreach (var role in roles)
            await TestRoleSeeder.AssignRoleAsync(_factory, userId, role, companyId);
        return client;
    }

    private static void AssertForbidden(HttpResponseMessage response) =>
        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"Expected 401/403 but got {(int)response.StatusCode} {response.StatusCode}");

    private static void AssertReachedHandler(HttpResponseMessage response) =>
        Assert.True(
            response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized),
            $"Expected the request to reach the handler but got {(int)response.StatusCode} {response.StatusCode}");

    private static MultipartFormDataContent BuildSubmission(Guid companyId) => new()
    {
        { new StringContent(companyId.ToString()), "CompanyId" },
        { new StringContent("AskQuestion"), "Type" },
        { new StringContent("Role boundary question"), "Title" },
        { new StringContent("Some description of the issue."), "Description" },
        { new StringContent("Low"), "Priority" },
        { new StringContent("false"), "IncludeDiagnostics" },
    };


    [Fact]
    public async Task HrAdministrator_Can_Submit_SupportRequest()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientWithRoles(companyId, SystemRoles.Employee, SystemRoles.HrAdministrator);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/support/requests", BuildSubmission(companyId));

        Assert.True(response.IsSuccessStatusCode, $"Expected success but got {(int)response.StatusCode}");
    }

    [Theory]
    [InlineData(TenantPersona.Employee)]
    [InlineData(TenantPersona.Manager)]
    [InlineData(TenantPersona.Recruiter)]
    [InlineData(TenantPersona.CompanyAdministratorOnly)]
    public async Task NonHr_Roles_Cannot_Submit_List_Or_Get_SupportRequests(TenantPersona persona)
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientWithRoles(companyId, RolesFor(persona));

        var submit = await client.PostAsync(
            $"/api/companies/{companyId}/support/requests", BuildSubmission(companyId));
        Assert.Equal(HttpStatusCode.Forbidden, submit.StatusCode);

        var list = await client.GetAsync($"/api/companies/{companyId}/support/requests");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

        var get = await client.GetAsync($"/api/companies/{companyId}/support/requests/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
    }

    [Theory]
    [InlineData(TenantPersona.Employee)]
    [InlineData(TenantPersona.Manager)]
    [InlineData(TenantPersona.Recruiter)]
    [InlineData(TenantPersona.CompanyAdministratorOnly)]
    public async Task NonHr_Roles_Cannot_Reply_Or_Reach_Dashboard(TenantPersona persona)
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientWithRoles(companyId, RolesFor(persona));

        var reply = await client.PostAsync(
            $"/api/companies/{companyId}/support/requests/{Guid.NewGuid()}/responses",
            BuildResponseBody(companyId, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Forbidden, reply.StatusCode);

        var dashboard = await client.GetAsync("/api/support/dashboard");
        Assert.Equal(HttpStatusCode.Forbidden, dashboard.StatusCode);
    }

    [Fact]
    public async Task Anonymous_Cannot_Submit_SupportRequest()
    {
        var companyId = Guid.NewGuid();
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/support/requests", BuildSubmission(companyId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HrAdministrator_Cannot_Submit_For_A_Different_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = await ClientWithRoles(companyId, SystemRoles.Employee, SystemRoles.HrAdministrator);

        var response = await client.PostAsync(
            $"/api/companies/{otherCompanyId}/support/requests", BuildSubmission(otherCompanyId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }


    [Fact]
    public async Task CompanyAdministratorOnly_CannotReach_SupportDashboard()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(CompanyAdminOnly, companyId, alsoHrAdmin: false);

        var response = await client.GetAsync("/api/support/dashboard");

        AssertForbidden(response);
    }

    [Fact]
    public async Task CompanyAdministratorPlusHrAdministrator_CanReach_SupportDashboard()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(CompanyAdminPlusHrAdmin, companyId, alsoHrAdmin: true);

        var response = await client.GetAsync("/api/support/dashboard");

        AssertReachedHandler(response);
    }


    private static MultipartFormDataContent BuildResponseBody(Guid companyId, Guid id) => new()
    {
        { new StringContent(companyId.ToString()), "CompanyId" },
        { new StringContent(id.ToString()), "Id" },
        { new StringContent("Reply body"), "BodyHtml" },
    };

    [Fact]
    public async Task CompanyAdministratorOnly_CannotReach_AddSupportResponse()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(CompanyAdminOnly, companyId, alsoHrAdmin: false);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/support/requests/{Guid.NewGuid()}/responses",
            BuildResponseBody(companyId, Guid.NewGuid()));

        AssertForbidden(response);
    }

    [Fact]
    public async Task CompanyAdministratorPlusHrAdministrator_CanReach_AddSupportResponse()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(CompanyAdminPlusHrAdmin, companyId, alsoHrAdmin: true);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/support/requests/{Guid.NewGuid()}/responses",
            BuildResponseBody(companyId, Guid.NewGuid()));

        AssertReachedHandler(response);
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TenantStatusRoute_DoesNotExist_ForAnyTenantRole_IncludingHrAdministrator(bool alsoHrAdmin)
    {
        var companyId = Guid.NewGuid();
        var userId = alsoHrAdmin ? CompanyAdminPlusHrAdmin : CompanyAdminOnly;
        using var client = await ClientFor(userId, companyId, alsoHrAdmin);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/support/requests/{Guid.NewGuid()}/status",
            new { CompanyId = companyId, Id = Guid.NewGuid(), Status = "Resolved" });

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            $"Expected 404/405 but got {(int)response.StatusCode} {response.StatusCode}");
    }
}
