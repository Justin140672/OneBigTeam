using System.Net;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ReportFavouritesEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public ReportFavouritesEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> ClientFor(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee, companyId);
        return client;
    }

    // ── Get favourites ────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_Favourites_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/reporting/favourites");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Favourites_Returns_Forbidden_For_Employee()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/favourites");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("Recruiter")]
    [InlineData("HrAdministrator")]
    public async Task Get_Favourites_Returns_Ok_For_Any_Reporting_Entitled_Role(string roleName)
    {
        var roleId = roleName switch
        {
            "Manager" => SystemRoles.Manager,
            "Recruiter" => SystemRoles.Recruiter,
            _ => SystemRoles.HrAdministrator,
        };

        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, roleId);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/favourites");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_Favourites_Returns_Empty_When_None_Added()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/favourites");

        var payload = await response.Content.ReadFromJsonAsync<FavouritesPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.ReportIds);
    }

    // ── Add favourite ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Add_Favourite_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsync(
            $"/api/companies/{Guid.NewGuid()}/reporting/favourites/employee-directory", EmptyJsonBody());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Add_Favourite_Returns_Forbidden_For_Employee()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee);
        using var client = await ClientFor(userId, companyId);

        var response = await client.PutAsync(
            $"/api/companies/{companyId}/reporting/favourites/employee-directory", EmptyJsonBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Add_Favourite_Then_Get_Returns_It_In_List()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var addResponse = await client.PutAsync(
            $"/api/companies/{companyId}/reporting/favourites/employee-directory", EmptyJsonBody());
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);

        var getResponse = await client.GetAsync($"/api/companies/{companyId}/reporting/favourites");
        var payload = await getResponse.Content.ReadFromJsonAsync<FavouritesPayload>();

        Assert.NotNull(payload);
        Assert.Contains("employee-directory", payload!.ReportIds);
    }

    [Fact]
    public async Task Add_Favourite_Is_Idempotent_When_Called_Twice()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var first = await client.PutAsync(
            $"/api/companies/{companyId}/reporting/favourites/employee-directory", EmptyJsonBody());
        var second = await client.PutAsync(
            $"/api/companies/{companyId}/reporting/favourites/employee-directory", EmptyJsonBody());

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var getResponse = await client.GetAsync($"/api/companies/{companyId}/reporting/favourites");
        var payload = await getResponse.Content.ReadFromJsonAsync<FavouritesPayload>();

        Assert.NotNull(payload);
        Assert.Single(payload!.ReportIds, id => id == "employee-directory");
    }

    // ── Remove favourite ──────────────────────────────────────────────────────

    [Fact]
    public async Task Remove_Favourite_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.DeleteAsync(
            $"/api/companies/{Guid.NewGuid()}/reporting/favourites/employee-directory");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Remove_Favourite_Returns_Forbidden_For_Employee()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee);
        using var client = await ClientFor(userId, companyId);

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/reporting/favourites/employee-directory");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Remove_Favourite_Succeeds_When_No_Matching_Favourite_Exists()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/reporting/favourites/never-favourited-report");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Remove_Favourite_Removes_It_From_Subsequent_Get()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        await client.PutAsync($"/api/companies/{companyId}/reporting/favourites/employee-directory", EmptyJsonBody());

        var removeResponse = await client.DeleteAsync(
            $"/api/companies/{companyId}/reporting/favourites/employee-directory");
        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);

        var getResponse = await client.GetAsync($"/api/companies/{companyId}/reporting/favourites");
        var payload = await getResponse.Content.ReadFromJsonAsync<FavouritesPayload>();

        Assert.NotNull(payload);
        Assert.DoesNotContain("employee-directory", payload!.ReportIds);
    }

    // AddReportFavourite's request is entirely route-bound (CompanyId, ReportId) with no JSON
    // body fields, but FastEndpoints still requires a valid Content-Type on PUT requests — an
    // HttpClient PUT with a null body sends no Content-Type header at all, which FastEndpoints
    // rejects with 415. Sending an empty JSON object with the standard content type satisfies
    // that without needing any body-less special-casing on the endpoint itself.
    private static StringContent EmptyJsonBody() => new("{}", Encoding.UTF8, "application/json");

    private sealed record FavouritesPayload(List<string> ReportIds);
}
