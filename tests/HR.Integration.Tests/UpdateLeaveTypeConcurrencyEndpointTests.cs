using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// Ticket 2 (optimistic concurrency rollout): LeaveType.version coverage for
// PUT .../leave-types/{id}, against the real Postgres-backed ApiWebApplicationFactory. Follows
// UpdateLeaveTypeEndpointTests for auth helpers. The pre-edit Version is read from the
// ListLeaveTypes item, which carries it.
[Collection("Integration")]
public class UpdateLeaveTypeConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("bbcc0003-0000-0000-0000-000000000001");

    public UpdateLeaveTypeConcurrencyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
        // ListLeaveTypes (used to read the pre-edit Version) is gated on role:employee.
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.Employee))
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Put_LeaveType_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/leave-types/{Guid.NewGuid()}", new { name = "X" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Two_Editors_Second_Stale_Save_Returns_409_Concurrency_And_First_Values_Preserved()
    {
        var (client, companyId, id) = await CreateLeaveTypeAsync();
        var version = (await GetItemAsync(client, companyId, id)).Version;

        var editorA = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/leave-types/{id}", Body(companyId, id, days: 28, expectedVersion: version));
        Assert.Equal(HttpStatusCode.OK, editorA.StatusCode);
        Assert.Equal(version + 1, (await editorA.Content.ReadFromJsonAsync<LeaveTypePayload>())!.Version);

        var editorB = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/leave-types/{id}", Body(companyId, id, days: 30, expectedVersion: version));

        Assert.Equal(HttpStatusCode.Conflict, editorB.StatusCode);
        Assert.Equal("concurrency", (await editorB.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);

        Assert.Equal(28, (await GetItemAsync(client, companyId, id)).DefaultEntitlementDays);
    }

    [Fact]
    public async Task Put_LeaveType_With_Correct_ExpectedVersion_Succeeds_And_Increments_Version()
    {
        var (client, companyId, id) = await CreateLeaveTypeAsync();
        var version = (await GetItemAsync(client, companyId, id)).Version;

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/leave-types/{id}", Body(companyId, id, days: 27, expectedVersion: version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = (await response.Content.ReadFromJsonAsync<LeaveTypePayload>())!;
        Assert.Equal(version + 1, payload.Version);
        Assert.Equal(27, payload.DefaultEntitlementDays);
    }

    [Fact]
    public async Task Put_LeaveType_Without_ExpectedVersion_Returns_422_And_Writes_Nothing()
    {
        var (client, companyId, id) = await CreateLeaveTypeAsync();

        var before = await GetItemAsync(client, companyId, id);

        var r1 = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/leave-types/{id}", Body(companyId, id, days: 21, expectedVersion: null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r1.StatusCode);

        var after = await GetItemAsync(client, companyId, id);
        Assert.Equal(before.DefaultEntitlementDays, after.DefaultEntitlementDays);
        Assert.Equal(before.Version, after.Version);
    }

    private static object Body(Guid companyId, Guid id, int days, int? expectedVersion)
        => new
        {
            companyId,
            id,
            name = "Annual Leave",
            code = "ANNUAL",
            defaultEntitlementDays = days,
            accrualMethod = "Monthly",
            behaviour = "Standard",
            expectedVersion
        };

    private async Task<(HttpClient Client, Guid CompanyId, Guid Id)> CreateLeaveTypeAsync()
    {
        var companyId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUserId, SystemRoles.HrAdministrator, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/leave-types", new
        {
            companyId,
            name = "Annual Leave",
            code = "ANNUAL",
            defaultEntitlementDays = 25,
            accrualMethod = "Monthly",
            behaviour = "Standard"
        });
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<LeaveTypePayload>())!;
        return (client, companyId, created.Id);
    }

    private static async Task<LeaveTypePayload> GetItemAsync(HttpClient client, Guid companyId, Guid id)
    {
        var response = await client.GetAsync($"/api/companies/{companyId}/leave-types");
        response.EnsureSuccessStatusCode();
        var payload = (await response.Content.ReadFromJsonAsync<ListPayload>())!;
        return payload.Items.Single(i => i.Id == id);
    }

    private sealed record ErrorPayload(string? Error, string? Code);
    private sealed record ListPayload(IReadOnlyList<LeaveTypePayload> Items);
    private sealed record LeaveTypePayload(Guid Id, string Name, string Code, int DefaultEntitlementDays, bool IsActive, int Version);
}
