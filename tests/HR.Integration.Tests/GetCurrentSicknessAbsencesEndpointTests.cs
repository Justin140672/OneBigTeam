using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// GET .../companies/{companyId}/sickness-records/current — the "who is off sick right now" view.
/// Gated by <c>sickness:manage</c>; returns only <see cref="HR.Modules.Sickness.Domain.SicknessStatus.Active"/>
/// records for the route company, ordered by start date.
/// </summary>
[Collection("Integration")]
public class GetCurrentSicknessAbsencesEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("cc000012-0000-0000-0000-000000000001");

    public GetCurrentSicknessAbsencesEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    private static string Url(Guid companyId) => $"/api/companies/{companyId}/sickness-records/current";

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUserId, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private async Task<HttpClient> ClientFor(Guid companyId, Guid userId, Guid roleId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, roleId, companyId);
        return client;
    }

    private async Task<Guid> CreateCategory(HttpClient client, Guid companyId)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/sickness-categories", new
        {
            companyId,
            name = $"Category-{Guid.NewGuid():N}",
            displayOrder = 1
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private async Task<Guid> CreateRecord(HttpClient client, Guid companyId, Guid employeeId, Guid categoryId, string startDate)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records",
            new { companyId, employeeId, categoryId, startDate, startDayPart = 0 });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private async Task CloseRecord(HttpClient client, Guid companyId, Guid employeeId, Guid recordId, string endDate)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records/{recordId}/close",
            new { companyId, employeeId, id = recordId, endDate, endDayPart = 0 });
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(Url(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.Employee);
        var response = await client.GetAsync(Url(companyId));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Empty_When_No_Active_Absences()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var payload = await client.GetFromJsonAsync<CurrentPayload>(Url(companyId));

        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Returns_Only_Active_Records_Ordered_By_StartDate()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var categoryId = await CreateCategory(client, companyId);

        var empA = Guid.NewGuid();
        var empB = Guid.NewGuid();
        var empClosed = Guid.NewGuid();

        var later  = await CreateRecord(client, companyId, empB, categoryId, "2026-07-10");
        var earlier = await CreateRecord(client, companyId, empA, categoryId, "2026-07-01");
        var closedId = await CreateRecord(client, companyId, empClosed, categoryId, "2026-06-01");
        await CloseRecord(client, companyId, empClosed, closedId, "2026-06-05");

        var payload = await client.GetFromJsonAsync<CurrentPayload>(Url(companyId));

        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Items.Count);
        Assert.DoesNotContain(payload.Items, i => i.RecordId == closedId);
        Assert.Equal(new[] { earlier, later }, payload.Items.Select(i => i.RecordId).ToArray());
        Assert.Equal("2026-07-01", payload.Items[0].StartDate);
    }

    [Fact]
    public async Task Is_Scoped_To_The_Route_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompany = Guid.NewGuid();

        using var otherClient = await AdminClient(otherCompany);
        var otherCategory = await CreateCategory(otherClient, otherCompany);
        await CreateRecord(otherClient, otherCompany, Guid.NewGuid(), otherCategory, "2026-07-01");

        using var client = await AdminClient(companyId);
        var payload = await client.GetFromJsonAsync<CurrentPayload>(Url(companyId));

        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    private sealed record IdPayload(Guid Id);
    private sealed record CurrentPayload(List<CurrentItem> Items);
    private sealed record CurrentItem(Guid RecordId, Guid EmployeeId, Guid CategoryId, string StartDate, string EvidenceStatus);
}
