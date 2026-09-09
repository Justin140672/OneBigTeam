using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// Ticket 2 (optimistic concurrency rollout): SicknessRecord.version coverage for
// PUT .../sickness-records/{id}, against the real Postgres-backed ApiWebApplicationFactory. Follows
// UpdateSicknessRecordEndpointTests for seeding/auth helpers.
[Collection("Integration")]
public class UpdateSicknessRecordConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("cccc0010-0000-0000-0000-000000000013");

    public UpdateSicknessRecordConcurrencyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Put_SicknessRecord_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/sickness-records/{Guid.NewGuid()}", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Two_Editors_Second_Stale_Save_Returns_409_Concurrency_And_First_Values_Preserved()
    {
        var (client, companyId, employeeId, categoryId, recordId) = await SeedAsync();
        var version = (await GetAsync(client, companyId, employeeId, recordId)).Version;

        var editorA = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records/{recordId}",
            Body(companyId, employeeId, recordId, categoryId, notes: "EditorA", expectedVersion: version));
        Assert.Equal(HttpStatusCode.OK, editorA.StatusCode);
        Assert.Equal(version + 1, (await editorA.Content.ReadFromJsonAsync<RecordPayload>())!.Version);

        var editorB = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records/{recordId}",
            Body(companyId, employeeId, recordId, categoryId, notes: "EditorB", expectedVersion: version));

        Assert.Equal(HttpStatusCode.Conflict, editorB.StatusCode);
        Assert.Equal("concurrency", (await editorB.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);

        Assert.Equal("EditorA", (await GetAsync(client, companyId, employeeId, recordId)).Notes);
    }

    [Fact]
    public async Task Put_SicknessRecord_With_Correct_ExpectedVersion_Succeeds_And_Increments_Version()
    {
        var (client, companyId, employeeId, categoryId, recordId) = await SeedAsync();
        var version = (await GetAsync(client, companyId, employeeId, recordId)).Version;

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records/{recordId}",
            Body(companyId, employeeId, recordId, categoryId, notes: "Updated", expectedVersion: version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = (await response.Content.ReadFromJsonAsync<RecordPayload>())!;
        Assert.Equal(version + 1, payload.Version);
        Assert.Equal("Updated", payload.Notes);
    }

    [Fact]
    public async Task Put_SicknessRecord_Without_ExpectedVersion_Returns_422_And_Writes_Nothing()
    {
        var (client, companyId, employeeId, categoryId, recordId) = await SeedAsync();

        var before = await GetAsync(client, companyId, employeeId, recordId);

        var r1 = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records/{recordId}",
            Body(companyId, employeeId, recordId, categoryId, notes: "NoVersion1", expectedVersion: null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r1.StatusCode);

        var after = await GetAsync(client, companyId, employeeId, recordId);
        Assert.Equal(before.Notes, after.Notes);
        Assert.Equal(before.Version, after.Version);
    }

    private static object Body(Guid companyId, Guid employeeId, Guid recordId, Guid categoryId, string notes, int? expectedVersion)
        => new
        {
            companyId,
            employeeId,
            id = recordId,
            categoryId,
            startDate = "2026-07-01",
            startDayPart = 0,
            notes,
            expectedVersion
        };

    private async Task<(HttpClient Client, Guid CompanyId, Guid EmployeeId, Guid CategoryId, Guid RecordId)> SeedAsync()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUserId, SystemRoles.HrAdministrator, companyId);

        var categoryResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/sickness-categories",
            new { companyId, name = $"Category-{Guid.NewGuid():N}", displayOrder = 1 });
        categoryResponse.EnsureSuccessStatusCode();
        var categoryId = (await categoryResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var recordResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records",
            new { companyId, employeeId, categoryId, startDate = "2026-07-01", startDayPart = 0 });
        recordResponse.EnsureSuccessStatusCode();
        var recordId = (await recordResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        return (client, companyId, employeeId, categoryId, recordId);
    }

    private static async Task<RecordPayload> GetAsync(HttpClient client, Guid companyId, Guid employeeId, Guid recordId)
    {
        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records/{recordId}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RecordPayload>())!;
    }

    private sealed record IdPayload(Guid Id);
    private sealed record ErrorPayload(string? Error, string? Code);
    private sealed record RecordPayload(Guid Id, Guid CategoryId, string? Notes, int Version);
}
