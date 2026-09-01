using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// GET .../companies/{companyId}/notifications/unread-count — the badge count for the current
/// user. Gated by <c>role:employee</c>; the count reflects only unread notifications for the
/// authenticated employee in the route company.
/// </summary>
[Collection("Integration")]
public class GetUnreadNotificationCountEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public GetUnreadNotificationCountEndpointTests(ApiWebApplicationFactory factory) => _factory = factory;

    private static string CountUrl(Guid companyId) => $"/api/companies/{companyId}/notifications/unread-count";
    private static string MyUrl(Guid companyId)    => $"/api/companies/{companyId}/notifications/my";

    private async Task<HttpClient> ClientFor(Guid companyId, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee, companyId);
        return client;
    }

    [Fact]
    public async Task Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(CountUrl(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Zero_When_The_User_Has_No_Notifications()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, Guid.NewGuid());

        var payload = await client.GetFromJsonAsync<CountPayload>(CountUrl(companyId));

        Assert.NotNull(payload);
        Assert.Equal(0, payload!.Count);
    }

    [Fact]
    public async Task Counts_Only_Unread_Notifications_For_The_Current_User()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        using var client = await ClientFor(companyId, userId);

        await TaskSeeder.SeedAsync(_factory, companyId, "Unread A", assignedEmployeeId: userId);
        await TaskSeeder.SeedAsync(_factory, companyId, "Unread B", assignedEmployeeId: userId);
        await TaskSeeder.SeedAsync(_factory, companyId, "Will be read", assignedEmployeeId: userId);

        // Mark one of the three read via the real endpoint.
        var myList = await client.GetFromJsonAsync<MyPayload>(MyUrl(companyId));
        var toRead = myList!.Items.First().Id;
        var markResp = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/notifications/{toRead}/read",
            new { companyId, notificationId = toRead });
        markResp.EnsureSuccessStatusCode();

        var payload = await client.GetFromJsonAsync<CountPayload>(CountUrl(companyId));

        Assert.Equal(2, payload!.Count);
    }

    [Fact]
    public async Task Does_Not_Count_Another_Users_Notifications()
    {
        var companyId = Guid.NewGuid();
        var me    = Guid.NewGuid();
        var other = Guid.NewGuid();
        using var client = await ClientFor(companyId, me);

        await TaskSeeder.SeedAsync(_factory, companyId, "For other", assignedEmployeeId: other);

        var payload = await client.GetFromJsonAsync<CountPayload>(CountUrl(companyId));

        Assert.Equal(0, payload!.Count);
    }

    [Fact]
    public async Task Does_Not_Count_Notifications_From_Another_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var userId   = Guid.NewGuid();

        // Same user has an unread notification in company B only.
        await TaskSeeder.SeedAsync(_factory, companyB, "Company B notification", assignedEmployeeId: userId);

        using var clientA = await ClientFor(companyA, userId);
        var payloadA = await clientA.GetFromJsonAsync<CountPayload>(CountUrl(companyA));
        Assert.Equal(0, payloadA!.Count);

        using var clientB = await ClientFor(companyB, userId);
        var payloadB = await clientB.GetFromJsonAsync<CountPayload>(CountUrl(companyB));
        Assert.Equal(1, payloadB!.Count);
    }

    private sealed record CountPayload(int Count);
    private sealed record MyPayload(int UnreadCount, List<MyItem> Items);
    private sealed record MyItem(Guid Id);
}
