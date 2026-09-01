using HR.Modules.Notifications.Features.NotifyOnOrganisationDataExportCompleted;
using HR.Modules.Notifications.Persistence;
using HR.Modules.Notifications.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Tests;

public class NotifyOnOrganisationDataExportCompletedHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Writes_Notification_To_The_Requesting_User()
    {
        await using var ctx = BuildContext();
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        var exportId  = Guid.NewGuid();

        var handler = Build(ctx);

        await handler.HandleAsync(
            new OrganisationDataExportCompletedIntegrationEvent(companyId, exportId, userId, Now),
            CancellationToken.None);

        var saved = await ctx.Notifications.SingleAsync();
        Assert.Equal(userId, saved.EmployeeId);
        Assert.Equal(exportId, saved.SourceEntityId);
        Assert.Equal(NotificationType.OrganisationDataExportReady, saved.Type);
        Assert.Equal("Your organisation data export is ready", saved.Title);
    }

    [Fact]
    public async Task HandleAsync_Skips_When_No_Requesting_User()
    {
        await using var ctx = BuildContext();
        var logger = new FakeLogger<NotifyOnOrganisationDataExportCompletedHandler>();
        var handler = new NotifyOnOrganisationDataExportCompletedHandler(
            new NotificationWriter(ctx, new NoOpBackgroundJobClient(), new FakeAuditPublisher(), new FakeCompanyNotificationSettingsReader()),
            logger);

        await handler.HandleAsync(
            new OrganisationDataExportCompletedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), null, Now),
            CancellationToken.None);

        Assert.Empty(ctx.Notifications);
        Assert.Single(logger.Messages);
    }

    [Fact]
    public async Task HandleAsync_Is_Idempotent_Against_Redelivery()
    {
        await using var ctx = BuildContext();
        var e = new OrganisationDataExportCompletedIntegrationEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

        var handler = Build(ctx);
        await handler.HandleAsync(e, CancellationToken.None);
        await handler.HandleAsync(e, CancellationToken.None);

        Assert.Single(ctx.Notifications);
    }

    private static NotifyOnOrganisationDataExportCompletedHandler Build(NotificationsDbContext ctx) =>
        new(new NotificationWriter(ctx, new NoOpBackgroundJobClient(), new FakeAuditPublisher(), new FakeCompanyNotificationSettingsReader()),
            new FakeLogger<NotifyOnOrganisationDataExportCompletedHandler>());

    private static NotificationsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NotificationsDbContext(options);
    }
}
