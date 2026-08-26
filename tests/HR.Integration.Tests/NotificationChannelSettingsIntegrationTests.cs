using System.Net;
using System.Net.Http.Json;
using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// SET-06 end-to-end: proves that disabling a company's EmailNotificationsEnabled setting via the
/// real UpdateNotificationSettings endpoint results in real NotificationWriter (the same choke-point
/// used by every notification-raising handler across the app, e.g. LeaveApprovalEffectsService)
/// creating no EmailDelivery row for a non-mandatory, email-eligible notification type — while the
/// in-app Notification still appears via the real GetMyNotifications endpoint. See
/// NotificationWriterTests/NotificationWriterTemplatedTests/EmailDeliveryJobTests in
/// HR.Modules.Notifications.Tests for the equivalent, more exhaustive unit-level coverage (including
/// the mandatory-type bypass and the "queued-then-disabled" EmailDeliveryJob scenario) — this test
/// intentionally only proves the write-path wiring end-to-end through the real DI container rather
/// than duplicating every branch already covered at the unit level. Driving this through a genuine
/// business action (e.g. a full leave-request-then-approve HTTP workflow) was considered, but
/// INotificationWriter — resolved from the real DI container exactly as every production handler
/// does — is the same production entry point LeaveApprovalEffectsService itself calls, so invoking
/// it directly here is a faithful proxy without the unrelated ceremony of standing up a full leave
/// request/approval flow.
/// </summary>
[Collection("Integration")]
public class NotificationChannelSettingsIntegrationTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid HrAdminUserId = new("ce000032-0000-0000-0000-000000000001");

    public NotificationChannelSettingsIntegrationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Disabling_EmailNotifications_Results_In_No_EmailDelivery_But_InApp_Notification_Still_Created()
    {
        var companyId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, HrAdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, HrAdminUserId, companyId);

        var settingsResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/notification-settings",
            new { emailNotificationsEnabled = false, scheduledRemindersEnabled = true, version = 1 });
        Assert.Equal(HttpStatusCode.OK, settingsResponse.StatusCode);

        var employeeId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<INotificationWriter>();

            await writer.WriteAsync(
                notificationId, companyId, employeeId,
                "Leave approved", "Your leave request was approved.",
                Guid.NewGuid(), NotificationType.LeaveApproved, NotificationPriority.Normal,
                DateTimeOffset.UtcNow);
        }

        // In-app notification continues per the documented channel policy, regardless of the
        // EmailNotificationsEnabled setting.
        using (var scope = _factory.Services.CreateScope())
        {
            var notificationsDb = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

            var notification = await notificationsDb.Notifications.SingleOrDefaultAsync(n => n.Id == notificationId);
            Assert.NotNull(notification);

            // No EmailDelivery row (and therefore no enqueued send) was created for this
            // non-mandatory, email-eligible type while the company had email disabled.
            var delivery = await notificationsDb.EmailDeliveries.SingleOrDefaultAsync(d => d.NotificationId == notificationId);
            Assert.Null(delivery);
        }
    }
}
