using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// HR-only "find a person" directory search:
/// GET /api/companies/{companyId}/employees/directory-search
/// Gated to <c>role:hr-administrator</c> — Manager / Recruiter / Company Administrator / Employee
/// and anonymous callers must all be rejected.
/// </summary>
[Collection("Integration")]
public class SearchEmployeeDirectoryEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid HrAdminA = new("5ea5c400-0000-0000-0000-000000000001");
    private static readonly Guid HrAdminB = new("5ea5c400-0000-0000-0000-000000000002");
    private static readonly Guid ManagerUser = new("5ea5c400-0000-0000-0000-000000000003");
    private static readonly Guid RecruiterUser = new("5ea5c400-0000-0000-0000-000000000004");
    private static readonly Guid CompanyAdminUser = new("5ea5c400-0000-0000-0000-000000000005");
    private static readonly Guid EmployeeUser = new("5ea5c400-0000-0000-0000-000000000006");

    public SearchEmployeeDirectoryEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminA, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminA, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminB, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminB, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, ManagerUser, SystemRoles.Manager);
            await TestRoleSeeder.AssignRoleAsync(factory, ManagerUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminUser, SystemRoles.CompanyAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, EmployeeUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientFor(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    private static string SearchUrl(Guid companyId, string? term = null, bool? includeLeavers = null, int? limit = null)
    {
        var query = new List<string>();
        if (term is not null) query.Add($"term={Uri.EscapeDataString(term)}");
        if (includeLeavers is not null) query.Add($"includeLeavers={includeLeavers.Value.ToString().ToLowerInvariant()}");
        if (limit is not null) query.Add($"limit={limit.Value}");
        var qs = query.Count > 0 ? "?" + string.Join("&", query) : string.Empty;
        return $"/api/companies/{companyId}/employees/directory-search{qs}";
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_DirectorySearch_Returns_Ok_And_Matching_Employees_For_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminA, companyId);

        var (aliceId, _) = await CompensationTestHelpers.CreateEmployeeWithNumberAsync(
            client, companyId, firstName: "Alice", lastName: "Zephyr");
        await CompensationTestHelpers.CreateEmployeeWithNumberAsync(
            client, companyId, firstName: "Bob", lastName: "Quill");

        var response = await client.GetAsync(SearchUrl(companyId, term: "zephyr"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SearchResponse>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal(aliceId, item.Id);
        Assert.Equal("Alice", item.FirstName);
        Assert.Equal("Zephyr", item.LastName);
    }

    // ── Authorization ────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_DirectorySearch_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(SearchUrl(Guid.NewGuid(), term: "smith"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public static IEnumerable<object[]> ForbiddenPersonas()
    {
        yield return new object[] { "manager" };
        yield return new object[] { "recruiter" };
        yield return new object[] { "company-admin" };
        yield return new object[] { "employee" };
    }

    [Theory]
    [MemberData(nameof(ForbiddenPersonas))]
    public async Task Get_DirectorySearch_Returns_Forbidden_For_NonHrAdministrator_Roles(string persona)
    {
        var userId = persona switch
        {
            "manager" => ManagerUser,
            "recruiter" => RecruiterUser,
            "company-admin" => CompanyAdminUser,
            "employee" => EmployeeUser,
            _ => throw new ArgumentOutOfRangeException(nameof(persona))
        };
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync(SearchUrl(companyId, term: "smith"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Company scoping ──────────────────────────────────────────────────────

    [Fact]
    public async Task Get_DirectorySearch_Does_Not_Return_Employees_From_Another_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        using var clientA = await ClientFor(HrAdminA, companyA);
        using var clientB = await ClientFor(HrAdminB, companyB);

        await CompensationTestHelpers.CreateEmployeeWithNumberAsync(
            clientA, companyA, firstName: "Alpha", lastName: "Sharedsurname");
        await CompensationTestHelpers.CreateEmployeeWithNumberAsync(
            clientB, companyB, firstName: "Beta", lastName: "Sharedsurname");

        var response = await clientA.GetAsync(SearchUrl(companyA, term: "sharedsurname"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SearchResponse>();
        var item = Assert.Single(payload!.Items);
        Assert.Equal("Alpha", item.FirstName);
    }

    // ── Leaver filtering ────────────────────────────────────────────────────

    [Fact]
    public async Task Get_DirectorySearch_Excludes_Leavers_Unless_IncludeLeavers_Requested()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminA, companyId);

        // Active / future-dated (Draft) employee — always visible.
        var (stayerId, _) = await CompensationTestHelpers.CreateEmployeeWithNumberAsync(
            client, companyId, firstName: "Stay", lastName: "Erson");

        var (leaverId, _) = await CompensationTestHelpers.CreateEmployeeWithNumberAsync(
            client, companyId, firstName: "Leave", lastName: "Erson");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startLeaving = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{leaverId}/leaving-process",
            new
            {
                companyId,
                employeeId = leaverId,
                resignationReceivedDate = today.AddDays(-7).ToString("yyyy-MM-dd"),
                leavingDate = today.AddDays(30).ToString("yyyy-MM-dd"),
                lastWorkingDay = today.AddDays(29).ToString("yyyy-MM-dd"),
                leavingReason = "Resignation"
            });
        startLeaving.EnsureSuccessStatusCode();

        var defaultResponse = await client.GetAsync(SearchUrl(companyId, term: "erson"));
        Assert.Equal(HttpStatusCode.OK, defaultResponse.StatusCode);
        var defaultPayload = await defaultResponse.Content.ReadFromJsonAsync<SearchResponse>();
        Assert.Equal(new[] { stayerId }, defaultPayload!.Items.Select(i => i.Id).ToArray());

        var inclResponse = await client.GetAsync(SearchUrl(companyId, term: "erson", includeLeavers: true));
        Assert.Equal(HttpStatusCode.OK, inclResponse.StatusCode);
        var inclPayload = await inclResponse.Content.ReadFromJsonAsync<SearchResponse>();
        Assert.Contains(inclPayload!.Items, i => i.Id == stayerId);
        Assert.Contains(inclPayload.Items, i => i.Id == leaverId);
    }

    // ── Validation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_DirectorySearch_Returns_ValidationError_When_Limit_Is_Zero()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminA, companyId);

        var response = await client.GetAsync(SearchUrl(companyId, term: "smith", limit: 0));

        Assert.True(
            response.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest,
            $"Expected 422/400 but got {(int)response.StatusCode} {response.StatusCode}");
    }

    private sealed record SearchResponse(IReadOnlyList<SearchItem> Items);

    private sealed record SearchItem(
        Guid Id,
        string FirstName,
        string LastName,
        string? EmployeeNumber,
        string? PositionProfileTitle,
        string? DepartmentName,
        string Status);
}
