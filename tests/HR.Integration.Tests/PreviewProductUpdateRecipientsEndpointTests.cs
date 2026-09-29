using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class PreviewProductUpdateRecipientsEndpointTests
{
    private const string Url = "/api/notifications/admin/product-updates/recipient-preview";

    private readonly ApiWebApplicationFactory _factory;

    public PreviewProductUpdateRecipientsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

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
    public async Task Get_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Returns_Forbidden_For_Authenticated_NonPlatformAdmin_Caller()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee, companyId);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Returns_Ok_With_Sane_Counts_For_PlatformAdmin()
    {
        var recipientCompanyId = Guid.NewGuid();
        var recipientUserId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(
            _factory, recipientUserId, SystemRoles.CompanyAdministrator, recipientCompanyId);

        using var client = await PlatformAdminClientAsync();

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PreviewPayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.RecipientCount >= 1);
        Assert.True(payload.CompanyCount >= 1);
        Assert.True(payload.CompanyCount <= payload.RecipientCount);
    }

    [Fact]
    public async Task Get_Does_Not_Create_Any_Notification_Rows()
    {
        var recipientCompanyId = Guid.NewGuid();
        var recipientUserId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(
            _factory, recipientUserId, SystemRoles.CompanyAdministrator, recipientCompanyId);

        using var client = await PlatformAdminClientAsync();

        var response = await client.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var notificationsDb = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var anyForRecipient = await notificationsDb.Notifications
            .AnyAsync(n => n.CompanyId == recipientCompanyId && n.EmployeeId == recipientUserId);

        Assert.False(anyForRecipient);
    }

    private sealed record PreviewPayload(int RecipientCount, int CompanyCount);
}
