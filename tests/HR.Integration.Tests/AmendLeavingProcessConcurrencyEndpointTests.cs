using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// Ticket 2 (optimistic concurrency rollout): EmployeeLeavingProcess.version coverage for
// PUT .../leaving-process, exercised against the real Postgres-backed ApiWebApplicationFactory
// where the concurrency token is genuinely enforced by the database. Follows
// AmendLeavingProcessEndpointTests for seeding/auth helpers and
// UpdateEmployeeProfileConcurrencyEndpointTests for the two-editor stale-save shape.
[Collection("Integration")]
public class AmendLeavingProcessConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("ffcc0000-3000-0000-0000-000000000001");
    private static readonly Guid User2 = new("ffcc0000-3000-0000-0000-000000000002");
    private static readonly Guid User3 = new("ffcc0000-3000-0000-0000-000000000003");

    private static readonly DateOnly OriginalLeavingDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(45);
    private static readonly DateOnly EditorALeavingDate = OriginalLeavingDate.AddDays(10);
    private static readonly DateOnly EditorBLeavingDate = OriginalLeavingDate.AddDays(20);

    public AmendLeavingProcessConcurrencyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            foreach (var u in new[] { User1, User2, User3 })
            {
                await TestRoleSeeder.AssignRoleAsync(factory, u, SystemRoles.HrAdministrator);
                await TestRoleSeeder.AssignRoleAsync(factory, u, SystemRoles.Employee);
            }
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Put_LeavingProcess_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/leaving-process",
            Body(companyId, Guid.NewGuid(), EditorALeavingDate, expectedVersion: 1));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Two_Editors_Second_Stale_Save_Returns_409_Concurrency_And_First_Values_Preserved()
    {
        var (client, companyId, employeeId) = await StartLeavingProcessAsync(User1);

        var loaded = await GetAsync(client, companyId, employeeId);
        var version = loaded.Version;

        var editorA = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            Body(companyId, employeeId, EditorALeavingDate, expectedVersion: version));
        Assert.Equal(HttpStatusCode.OK, editorA.StatusCode);
        var editorAPayload = (await editorA.Content.ReadFromJsonAsync<AmendPayload>())!;
        Assert.Equal(version + 1, editorAPayload.Version);

        var editorB = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            Body(companyId, employeeId, EditorBLeavingDate, expectedVersion: version));

        Assert.Equal(HttpStatusCode.Conflict, editorB.StatusCode);
        var error = await editorB.Content.ReadFromJsonAsync<ErrorPayload>();
        Assert.Equal("concurrency", error!.Code);

        var after = await GetAsync(client, companyId, employeeId);
        Assert.Equal(EditorALeavingDate, after.LeavingDate);
    }

    [Fact]
    public async Task Put_LeavingProcess_With_Correct_ExpectedVersion_Succeeds_And_Increments_Version()
    {
        var (client, companyId, employeeId) = await StartLeavingProcessAsync(User2);
        var loaded = await GetAsync(client, companyId, employeeId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            Body(companyId, employeeId, EditorALeavingDate, expectedVersion: loaded.Version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = (await response.Content.ReadFromJsonAsync<AmendPayload>())!;
        Assert.Equal(loaded.Version + 1, payload.Version);
        Assert.Equal(EditorALeavingDate, payload.LeavingDate);
    }

    [Fact]
    public async Task Put_LeavingProcess_Without_ExpectedVersion_Returns_422_And_Writes_Nothing()
    {
        var (client, companyId, employeeId) = await StartLeavingProcessAsync(User3);

        var before = await GetAsync(client, companyId, employeeId);

        var r1 = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            Body(companyId, employeeId, EditorALeavingDate, expectedVersion: null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r1.StatusCode);

        var after = await GetAsync(client, companyId, employeeId);
        Assert.Equal(before.LeavingDate, after.LeavingDate);
        Assert.Equal(before.Version, after.Version);
    }

    private static object Body(Guid companyId, Guid employeeId, DateOnly leavingDate, int? expectedVersion)
        => new
        {
            companyId,
            employeeId,
            leavingDate = leavingDate.ToString("yyyy-MM-dd"),
            lastWorkingDay = leavingDate.AddDays(-1).ToString("yyyy-MM-dd"),
            leavingReason = "MutualAgreement",
            expectedVersion
        };

    private async Task<(HttpClient Client, Guid CompanyId, Guid EmployeeId)> StartLeavingProcessAsync(Guid userId)
    {
        var companyId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee, companyId);

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        var createResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Amend", "Employee", $"amend.{Guid.NewGuid():N}@example.com"));
        createResponse.EnsureSuccessStatusCode();
        var employeeId = (await createResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var startResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                resignationReceivedDate = OriginalLeavingDate.AddDays(-30).ToString("yyyy-MM-dd"),
                leavingDate = OriginalLeavingDate.ToString("yyyy-MM-dd"),
                lastWorkingDay = OriginalLeavingDate.AddDays(-1).ToString("yyyy-MM-dd"),
                leavingReason = "Resignation"
            });
        startResponse.EnsureSuccessStatusCode();

        return (client, companyId, employeeId);
    }

    private static async Task<LeavingProcessPayload> GetAsync(HttpClient client, Guid companyId, Guid employeeId)
    {
        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/leaving-process");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LeavingProcessPayload>())!;
    }

    private sealed record IdPayload(Guid Id);
    private sealed record ErrorPayload(string? Error, string? Code);
    private sealed record AmendPayload(Guid Id, Guid CompanyId, Guid EmployeeId, DateOnly LeavingDate, DateOnly LastWorkingDay, string LeavingReason, string Status, int Version);
    private sealed record LeavingProcessPayload(Guid Id, DateOnly LeavingDate, DateOnly LastWorkingDay, string LeavingReason, string Status, int Version);
}
