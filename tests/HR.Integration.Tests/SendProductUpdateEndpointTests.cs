using System.Net;
using System.Net.Http.Json;
using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class SendProductUpdateEndpointTests
{
    private const string Url = "/api/notifications/admin/product-updates";

    private readonly ApiWebApplicationFactory _factory;

    public SendProductUpdateEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static object ValidBody(string? url = "/reports/recruitment-pipeline") => new
    {
        title = "New feature: better reporting",
        message = "We've shipped an improved reporting dashboard for all customers.",
        url,
    };

    private async Task<HttpClient> PlatformAdminClientAsync()
    {
        var userId = Guid.NewGuid();
        await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory,
            PlatformAdministratorRole.SupportStaff,
            isEnabled: true,
            supabaseAuthUserId: userId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        return client;
    }

    [Fact]
    public async Task Post_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(Url, ValidBody());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns_Forbidden_For_Authenticated_NonPlatformAdmin_Caller()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee, companyId);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PostAsJsonAsync(Url, ValidBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns_UnprocessableEntity_When_Title_Is_Missing()
    {
        using var client = await PlatformAdminClientAsync();

        var body = new { title = "", message = "A message", url = (string?)null };
        var response = await client.PostAsJsonAsync(Url, body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns_UnprocessableEntity_When_Message_Is_Missing()
    {
        using var client = await PlatformAdminClientAsync();

        var body = new { title = "A title", message = "", url = (string?)null };
        var response = await client.PostAsJsonAsync(Url, body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns_UnprocessableEntity_When_Url_Is_Invalid()
    {
        using var client = await PlatformAdminClientAsync();

        var response = await client.PostAsJsonAsync(Url, ValidBody(url: "not-a-relative-path"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Sends_Notification_To_Eligible_Company_Administrator_And_It_Is_Visible_Via_Existing_Endpoints()
    {
        var recipientCompanyId = Guid.NewGuid();
        var recipientUserId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(
            _factory, recipientUserId, SystemRoles.CompanyAdministrator, recipientCompanyId);
        await TestRoleSeeder.AssignRoleAsync(
            _factory, recipientUserId, SystemRoles.Employee, recipientCompanyId);

        using var adminClient = await PlatformAdminClientAsync();

        var response = await adminClient.PostAsJsonAsync(
            Url, ValidBody(url: "/reports/recruitment-pipeline"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SendResponsePayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.RecipientCount >= 1);
        Assert.True(payload.CompanyCount >= 1);

        using var scope = _factory.Services.CreateScope();
        var notificationsDb = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var notification = await notificationsDb.Notifications
            .Where(n => n.CompanyId == recipientCompanyId
                        && n.EmployeeId == recipientUserId
                        && n.Type == NotificationType.ProductUpdate)
            .SingleOrDefaultAsync();

        Assert.NotNull(notification);
        Assert.Equal("New feature: better reporting", notification!.Title);
        Assert.Equal("We've shipped an improved reporting dashboard for all customers.", notification.Body);
        Assert.Equal("/reports/recruitment-pipeline", notification.ActionUrl);

        using var recipientClient = _factory.CreateClient();
        recipientClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, recipientUserId.ToString());
        recipientClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, recipientCompanyId.ToString());

        var unreadResp = await recipientClient.GetAsync(
            $"/api/companies/{recipientCompanyId}/notifications/unread-count");
        Assert.Equal(HttpStatusCode.OK, unreadResp.StatusCode);
        var unreadPayload = await unreadResp.Content.ReadFromJsonAsync<UnreadCountPayload>();
        Assert.True(unreadPayload!.Count >= 1);

        var myResp = await recipientClient.GetAsync(
            $"/api/companies/{recipientCompanyId}/notifications/my");
        var myPayload = await myResp.Content.ReadFromJsonAsync<NotifListPayload>();
        Assert.Contains(myPayload!.Items, i => i.Title == "New feature: better reporting" && i.Type == "ProductUpdate");
    }

    private sealed record SendResponsePayload(int RecipientCount, int CompanyCount);

    private sealed record UnreadCountPayload(int Count);

    private sealed record NotifListPayload(int UnreadCount, IReadOnlyList<NotifItem> Items);

    private sealed record NotifItem(Guid Id, string Title, bool IsRead, string Type);
}
