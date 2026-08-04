using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.SharedKernel;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class NotificationsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser       = Guid.Parse("11100004-0000-0000-0000-000000000001");
    private static readonly Guid SeededCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public NotificationsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    // ── GetMyNotifications ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMyNotifications_Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response     = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/notifications/my");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMyNotifications_Returns_Empty_When_No_Notifications()
    {
        var userId       = Guid.NewGuid();
        using var client = await AuthenticatedClient(userId);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/notifications/my");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<NotifListPayload>();
        Assert.Equal(0, payload!.UnreadCount);
        Assert.Empty(payload.Items);
    }

    [Fact]
    public async Task GetMyNotifications_Returns_Notification_When_Task_Assigned_To_Me()
    {
        // userId == employeeId — sub claim is used as employeeId
        var userId       = Guid.NewGuid();
        using var client = await AuthenticatedClient(userId);

        await TaskSeeder.SeedAsync(_factory, SeededCompanyId,
            title: "Notification test task",
            assignedEmployeeId: userId);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/notifications/my");
        var payload  = await response.Content.ReadFromJsonAsync<NotifListPayload>();

        Assert.Equal(1, payload!.UnreadCount);
        var notif = Assert.Single(payload.Items);
        Assert.False(notif.IsRead);
        Assert.Equal("TaskAssigned", notif.Type);
        Assert.Contains("Notification test task", notif.Title);
    }

    [Fact]
    public async Task GetMyNotifications_Returns_Notifications_Newest_First()
    {
        var userId       = Guid.NewGuid();
        using var client = await AuthenticatedClient(userId);

        await TaskSeeder.SeedAsync(_factory, SeededCompanyId, "First task",  assignedEmployeeId: userId);
        await TaskSeeder.SeedAsync(_factory, SeededCompanyId, "Second task", assignedEmployeeId: userId);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/notifications/my");
        var payload  = await response.Content.ReadFromJsonAsync<NotifListPayload>();

        Assert.Equal(2, payload!.UnreadCount);
        Assert.Equal("New task assigned: Second task", payload.Items[0].Title);
        Assert.Equal("New task assigned: First task",  payload.Items[1].Title);
    }

    [Fact]
    public async Task GetMyNotifications_Does_Not_Return_Other_Employees_Notifications()
    {
        var userA        = Guid.NewGuid();
        var userB        = Guid.NewGuid();
        using var client = await AuthenticatedClient(userA);

        await TaskSeeder.SeedAsync(_factory, SeededCompanyId, "Task for B", assignedEmployeeId: userB);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/notifications/my");
        var payload  = await response.Content.ReadFromJsonAsync<NotifListPayload>();

        Assert.Empty(payload!.Items);
    }

    // ── MarkNotificationRead ─────────────────────────────────────────────────────

    [Fact]
    public async Task MarkNotificationRead_Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response     = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/notifications/{Guid.NewGuid()}/read", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MarkNotificationRead_Returns_NotFound_For_Unknown_Notification()
    {
        using var client = await AuthenticatedClient(Guid.NewGuid());
        var response     = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/notifications/{Guid.NewGuid()}/read",
            new { companyId = SeededCompanyId, notificationId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MarkNotificationRead_Returns_NoContent_And_Decrements_UnreadCount()
    {
        var userId       = Guid.NewGuid();
        using var client = await AuthenticatedClient(userId);

        await TaskSeeder.SeedAsync(_factory, SeededCompanyId, "Mark-read test", assignedEmployeeId: userId);

        var listResp    = await client.GetAsync($"/api/companies/{SeededCompanyId}/notifications/my");
        var listPayload = await listResp.Content.ReadFromJsonAsync<NotifListPayload>();
        var notifId     = listPayload!.Items[0].Id;

        var markResp = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/notifications/{notifId}/read",
            new { companyId = SeededCompanyId, notificationId = notifId });
        Assert.Equal(HttpStatusCode.NoContent, markResp.StatusCode);

        var afterResp    = await client.GetAsync($"/api/companies/{SeededCompanyId}/notifications/my");
        var afterPayload = await afterResp.Content.ReadFromJsonAsync<NotifListPayload>();
        Assert.Equal(0, afterPayload!.UnreadCount);
        Assert.True(afterPayload.Items[0].IsRead);
    }

    // ── MarkAllNotificationsRead ─────────────────────────────────────────────────

    [Fact]
    public async Task MarkAllNotificationsRead_Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response     = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{Guid.NewGuid()}/notifications/read-all", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MarkAllNotificationsRead_Returns_NoContent_And_Clears_Unread_Count()
    {
        var userId       = Guid.NewGuid();
        using var client = await AuthenticatedClient(userId);

        await TaskSeeder.SeedAsync(_factory, SeededCompanyId, "Bulk read A", assignedEmployeeId: userId);
        await TaskSeeder.SeedAsync(_factory, SeededCompanyId, "Bulk read B", assignedEmployeeId: userId);
        await TaskSeeder.SeedAsync(_factory, SeededCompanyId, "Bulk read C", assignedEmployeeId: userId);

        var markAllResp = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{userId}/notifications/read-all",
            new { companyId = SeededCompanyId, employeeId = userId });
        Assert.Equal(HttpStatusCode.NoContent, markAllResp.StatusCode);

        var listResp = await client.GetAsync($"/api/companies/{SeededCompanyId}/notifications/my");
        var payload  = await listResp.Content.ReadFromJsonAsync<NotifListPayload>();
        Assert.Equal(0, payload!.UnreadCount);
        Assert.All(payload.Items, n => Assert.True(n.IsRead));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private async Task<HttpClient> AuthenticatedClient(Guid userId)
    {
        TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee).GetAwaiter().GetResult();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee, SeededCompanyId);
        return client;
    }

    private sealed record NotifListPayload(int UnreadCount, IReadOnlyList<NotifItem> Items);
    private sealed record NotifItem(Guid Id, string Title, bool IsRead, string Type);
}
