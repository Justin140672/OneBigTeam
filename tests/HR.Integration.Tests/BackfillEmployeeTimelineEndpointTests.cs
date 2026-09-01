using System.Net;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Integration coverage for the BackfillEmployeeTimeline slice
/// (POST /api/companies/{companyId}/employees/timeline/backfill), which replays historical
/// records into the employee timeline across 7 sources.
/// </summary>
[Collection("Integration")]
public class BackfillEmployeeTimelineEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid AdminUser = new("b0f10001-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("b0f10001-0000-0000-0000-000000000002");

    private static readonly string[] ExpectedSources =
    [
        "EmployeeCreated",
        "EmployeePromoted",
        "CompensationChanged",
        "ProbationPassed",
        "OnboardingCompleted",
        "SharedCompanyDocumentAcknowledged",
        "OffboardingStarted",
    ];

    public BackfillEmployeeTimelineEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, PlainEmployeeUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUser, SystemRoles.HrAdministrator, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUser, SystemRoles.Employee, companyId);
        return client;
    }

    private async Task<Guid> CreateEmployeeAsync(HttpClient admin, Guid companyId)
    {
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(admin, companyId);
        var response = await admin.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Hattie", "History", $"hattie.{Guid.NewGuid():N}@example.com"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private async Task<int> CountJoinedEntriesAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        return await db.EmployeeTimelineEntries
            .AsNoTracking()
            .CountAsync(e => e.CompanyId == companyId
                          && e.EventType == EmployeeTimelineEventType.EmployeeJoined);
    }

    [Fact]
    public async Task Post_Backfill_Returns_Unauthorized_For_Anonymous()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/timeline/backfill",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Backfill_Returns_Forbidden_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, PlainEmployeeUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, PlainEmployeeUser, SystemRoles.Employee, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/timeline/backfill", new { companyId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Backfill_Runs_All_Seven_Sources_Without_Failures_And_Ensures_Joined_Entry()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        await CreateEmployeeAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/timeline/backfill", new { companyId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<BackfillPayload>();
        Assert.NotNull(payload);
        Assert.Equal(companyId, payload!.CompanyId);

        Assert.Equal(
            ExpectedSources.OrderBy(s => s),
            payload.Sources.Select(s => s.Source).OrderBy(s => s));
        Assert.Equal(0, payload.TotalFailed);
        Assert.All(payload.Sources, s => Assert.Equal(0, s.Failed));

        // Regardless of whether the live CreateEmployee handler already wrote the "joined" entry,
        // exactly one must exist for the employee after the backfill.
        Assert.Equal(1, await CountJoinedEntriesAsync(companyId));
    }

    [Fact]
    public async Task Post_Backfill_Is_Idempotent_Second_Run_Creates_Nothing()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        await CreateEmployeeAsync(client, companyId);

        var first = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/timeline/backfill", new { companyId });
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/timeline/backfill", new { companyId });
        second.EnsureSuccessStatusCode();

        var payload = await second.Content.ReadFromJsonAsync<BackfillPayload>();
        Assert.Equal(0, payload!.TotalCreated);
        Assert.Equal(0, payload.TotalFailed);
        Assert.Equal(1, await CountJoinedEntriesAsync(companyId));
    }

    [Fact]
    public async Task Post_Backfill_For_Company_With_No_Employees_Succeeds_With_Zero_Totals()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/timeline/backfill", new { companyId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<BackfillPayload>();
        Assert.Equal(0, payload!.TotalCreated);
        Assert.Equal(0, payload.TotalSkipped);
        Assert.Equal(0, payload.TotalFailed);
    }

    [Fact]
    public async Task Post_Backfill_Returns_Forbidden_When_Route_Company_Does_Not_Match_Tenant()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/timeline/backfill", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record IdPayload(Guid Id);
    private sealed record BackfillSourcePayload(string Source, int Created, int Skipped, int Failed);
    private sealed record BackfillPayload(
        Guid CompanyId,
        List<BackfillSourcePayload> Sources,
        int TotalCreated,
        int TotalSkipped,
        int TotalFailed);
}
