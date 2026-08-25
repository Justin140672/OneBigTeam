using System.Net;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Persistence;
using Microsoft.Extensions.DependencyInjection;

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

    [Fact]
    public async Task Add_Favourite_Returns_BadRequest_For_Unknown_Report_Id()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var response = await client.PutAsync(
            $"/api/companies/{companyId}/reporting/favourites/not-a-real-report", EmptyJsonBody());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Add_Favourite_Returns_Forbidden_When_Caller_Lacks_Access_To_The_Reports_Gate()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        // HrAdministrator only — no Recruiter role, so lacks reporting:view-recruitment, which
        // "recruitment-pipeline-summary" requires. Still satisfies the endpoint-level
        // "reporting:view" policy, so this exercises the handler's per-report access-gate check.
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var response = await client.PutAsync(
            $"/api/companies/{companyId}/reporting/favourites/recruitment-pipeline-summary", EmptyJsonBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Favourites_Omits_A_Favourite_The_Caller_Is_No_Longer_Authorized_To_View()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        // Seed directly into the ReportingDbContext rather than through the Add endpoint: the Add
        // endpoint itself now (by design) refuses to add a favourite for a report the caller isn't
        // authorized for, so the only way to exercise GetReportFavourites' "no longer accessible"
        // filtering path is to persist the row directly, as if it had been added under a permission
        // the caller has since lost.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            db.ReportFavourites.Add(ReportFavourite.Create(
                Guid.NewGuid(), companyId, userId, "employee-directory", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        // Employee role satisfies none of the reporting:view-* gates (and not even the baseline
        // "reporting:view" policy — see Get_Favourites_Returns_Forbidden_For_Employee above), so
        // this exercises the "not authorized" filtering by seeding data for a user/company that a
        // Manager (who does pass the baseline policy but has no HR-gated access) then queries.
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Manager, companyId);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/favourites");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<FavouritesPayload>();
        Assert.NotNull(payload);
        Assert.DoesNotContain("employee-directory", payload!.ReportIds);
    }

    [Fact]
    public async Task Get_Favourites_Omits_A_Favourite_For_A_Report_No_Longer_In_The_Catalogue()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            db.ReportFavourites.Add(ReportFavourite.Create(
                Guid.NewGuid(), companyId, userId, "retired-report", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/favourites");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<FavouritesPayload>();
        Assert.NotNull(payload);
        Assert.DoesNotContain("retired-report", payload!.ReportIds);
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
