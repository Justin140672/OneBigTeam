using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.SharedKernel;

namespace HR.Integration.Tests;

public class NotificationsEndpointTests : IClassFixture<ApiWebApplicationFactory>
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

    // ── ListNotifications ────────────────────────────────────────────────────────

    [Fact]
    public async Task ListNotifications_Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response     = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{Guid.NewGuid()}/notifications");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListNotifications_Returns_Empty_When_No_Notifications()
    {
        var employeeId   = Guid.NewGuid();
        using var client = AuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employeeId}/notifications");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<NotifListPayload>();
        Assert.Equal(0, payload!.UnreadCount);
        Assert.Empty(payload.Items);
    }

    [Fact]
    public async Task ListNotifications_Returns_Notification_When_Task_Assigned_To_Employee()
    {
        var employeeId   = Guid.NewGuid();
        using var client = AuthenticatedClient(Guid.NewGuid());

        // Seed a task assigned to the employee — TaskCreator creates a notification alongside
        await TaskSeeder.SeedAsync(_factory, SeededCompanyId,
            title: "Notification test task",
            assignedEmployeeId: employeeId);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employeeId}/notifications");
        var payload  = await response.Content.ReadFromJsonAsync<NotifListPayload>();

        Assert.Equal(1, payload!.UnreadCount);
        var notif = Assert.Single(payload.Items);
        Assert.False(notif.IsRead);
        Assert.Equal("TaskAssigned", notif.Type);
        Assert.Contains("Notification test task", notif.Title);
    }

    [Fact]
    public async Task ListNotifications_Returns_Notifications_Newest_First()
    {
        var employeeId   = Guid.NewGuid();
        using var client = AuthenticatedClient(Guid.NewGuid());

        await TaskSeeder.SeedAsync(_factory, SeededCompanyId, "First task",  assignedEmployeeId: employeeId);
        await TaskSeeder.SeedAsync(_factory, SeededCompanyId, "Second task", assignedEmployeeId: employeeId);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employeeId}/notifications");
        var payload  = await response.Content.ReadFromJsonAsync<NotifListPayload>();

        Assert.Equal(2, payload!.UnreadCount);
        // Most recently created should appear first
        Assert.Equal("New task assigned: Second task", payload.Items[0].Title);
        Assert.Equal("New task assigned: First task",  payload.Items[1].Title);
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
        using var client = AuthenticatedClient(Guid.NewGuid());
        var response     = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/notifications/{Guid.NewGuid()}/read",
            new { companyId = SeededCompanyId, notificationId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MarkNotificationRead_Returns_NoContent_And_Decrements_UnreadCount()
    {
        var employeeId   = Guid.NewGuid();
        using var client = AuthenticatedClient(Guid.NewGuid());

        await TaskSeeder.SeedAsync(_factory, SeededCompanyId, "Mark-read test", assignedEmployeeId: employeeId);

        // Get the notification ID
        var listResp  = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employeeId}/notifications");
        var listPayload = await listResp.Content.ReadFromJsonAsync<NotifListPayload>();
        var notifId     = listPayload!.Items[0].Id;

        // Mark it as read
        var markResp = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/notifications/{notifId}/read",
            new { companyId = SeededCompanyId, notificationId = notifId });
        Assert.Equal(HttpStatusCode.NoContent, markResp.StatusCode);

        // Verify unread count dropped to 0
        var afterResp    = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employeeId}/notifications");
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
        var employeeId   = Guid.NewGuid();
        using var client = AuthenticatedClient(Guid.NewGuid());

        await TaskSeeder.SeedAsync(_factory, SeededCompanyId, "Bulk read A", assignedEmployeeId: employeeId);
        await TaskSeeder.SeedAsync(_factory, SeededCompanyId, "Bulk read B", assignedEmployeeId: employeeId);
        await TaskSeeder.SeedAsync(_factory, SeededCompanyId, "Bulk read C", assignedEmployeeId: employeeId);

        var markAllResp = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employeeId}/notifications/read-all",
            new { companyId = SeededCompanyId, employeeId });
        Assert.Equal(HttpStatusCode.NoContent, markAllResp.StatusCode);

        var listResp = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employeeId}/notifications");
        var payload  = await listResp.Content.ReadFromJsonAsync<NotifListPayload>();
        Assert.Equal(0, payload!.UnreadCount);
        Assert.All(payload.Items, n => Assert.True(n.IsRead));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private HttpClient AuthenticatedClient(Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        return client;
    }

    private sealed record NotifListPayload(int UnreadCount, IReadOnlyList<NotifItem> Items);
    private sealed record NotifItem(Guid Id, string Title, bool IsRead, string Type);
}
