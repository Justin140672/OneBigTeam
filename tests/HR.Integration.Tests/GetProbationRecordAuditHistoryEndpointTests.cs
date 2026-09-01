using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// AUD-07: GET .../companies/{companyId}/probation-records/{probationRecordId}/audit-history.
/// Gated by <c>probation:manage</c>. Reads the real AuditDbContext via IAuditHistoryReader,
/// newest-first, scoped by companyId.
///
/// Red until the foreign AUD-04 "actor_type" audit migration is fixed. Write the test correctly anyway.
/// </summary>
[Collection("Integration")]
public class GetProbationRecordAuditHistoryEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("cc000014-0000-0000-0000-000000000001");

    public GetProbationRecordAuditHistoryEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    private static string Url(Guid companyId, Guid recordId) =>
        $"/api/companies/{companyId}/probation-records/{recordId}/audit-history";

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUserId, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private async Task<Guid> CreateRecord(HttpClient client, Guid companyId, Guid employeeId)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-records", new
        {
            companyId,
            employeeId,
            managerEmployeeId = Guid.NewGuid(),
            startDate = "2026-06-01",
            expectedEndDate = "2026-09-01",
            notes = "seed"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    [Fact]
    public async Task Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(Url(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        var user = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, user, SystemRoles.Employee, companyId);

        var response = await client.GetAsync(Url(companyId, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        client.Dispose();
    }

    [Fact]
    public async Task Returns_Ordered_History_For_A_Probation_Record()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var recordId = await CreateRecord(client, companyId, Guid.NewGuid());

        var updateResp = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{recordId}",
            new { companyId, id = recordId, managerEmployeeId = Guid.NewGuid(), expectedEndDate = "2026-10-01", notes = "amended" });
        updateResp.EnsureSuccessStatusCode();

        var response = await client.GetAsync(Url(companyId, recordId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<HistoryPayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.Items.Count >= 2, $"expected >= 2 audit items, got {payload.Items.Count}");

        var occurred = payload.Items.Select(i => i.OccurredAt).ToList();
        Assert.True(occurred.SequenceEqual(occurred.OrderByDescending(x => x)), "items should be newest-first");
    }

    [Fact]
    public async Task Returns_Empty_History_For_Unknown_Record()
    {
        // Handler always returns Result.Success -> 200 + empty Items for an unknown id (not 404).
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.GetAsync(Url(companyId, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<HistoryPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Does_Not_Return_History_For_A_Record_In_Another_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompany = Guid.NewGuid();

        using var otherClient = await AdminClient(otherCompany);
        var recordId = await CreateRecord(otherClient, otherCompany, Guid.NewGuid());

        using var client = await AdminClient(companyId);
        var response = await client.GetAsync(Url(companyId, recordId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<HistoryPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    private sealed record IdPayload(Guid Id);
    private sealed record HistoryPayload(List<HistoryItem> Items);
    private sealed record HistoryItem(DateTimeOffset OccurredAt, string Action, string User);
}
