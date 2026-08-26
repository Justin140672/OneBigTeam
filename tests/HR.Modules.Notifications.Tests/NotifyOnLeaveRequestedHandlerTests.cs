using HR.Modules.Notifications.Features.NotifyOnLeaveRequested;
using HR.Modules.Notifications.Persistence;
using HR.Modules.Notifications.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Tests;

public class NotifyOnLeaveRequestedHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Writes_Notification_To_Manager()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId  = Guid.NewGuid();
        var requestId  = Guid.NewGuid();

        var managerReader = new FakeManagerReader { ManagerId = managerId };
        var nameReader     = new FakeEmployeeNameReader();
        nameReader.Names[employeeId] = "Alex Doe";

        var writer  = new NotificationWriter(ctx, new NoOpBackgroundJobClient(), new FakeAuditPublisher(), new FakeCompanyNotificationSettingsReader());
        var handler = new NotifyOnLeaveRequestedHandler(
            writer, managerReader, nameReader, new FakeLogger<NotifyOnLeaveRequestedHandler>());

        await handler.HandleAsync(
            new LeaveRequestedIntegrationEvent(
                companyId, employeeId, requestId, Guid.NewGuid(),
                new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 3), 3m, Now),
            CancellationToken.None);

        var saved = await ctx.Notifications.SingleAsync();
        Assert.Equal(managerId, saved.EmployeeId);
        Assert.Equal(requestId, saved.SourceEntityId);
        Assert.Equal(NotificationType.LeaveRequested, saved.Type);
        Assert.Contains("Alex Doe", saved.Body);
    }

    [Fact]
    public async Task HandleAsync_Skips_And_Logs_When_No_Manager()
    {
        await using var ctx = BuildContext();
        var managerReader = new FakeManagerReader { ManagerId = null };
        var logger = new FakeLogger<NotifyOnLeaveRequestedHandler>();

        var writer  = new NotificationWriter(ctx, new NoOpBackgroundJobClient(), new FakeAuditPublisher(), new FakeCompanyNotificationSettingsReader());
        var handler = new NotifyOnLeaveRequestedHandler(
            writer, managerReader, new FakeEmployeeNameReader(), logger);

        await handler.HandleAsync(
            new LeaveRequestedIntegrationEvent(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 3), 3m, Now),
            CancellationToken.None);

        Assert.Empty(ctx.Notifications);
        Assert.Single(logger.Messages);
    }

    [Fact]
    public async Task HandleAsync_Is_Idempotent_Against_Redelivery()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId  = Guid.NewGuid();
        var requestId  = Guid.NewGuid();

        var managerReader = new FakeManagerReader { ManagerId = managerId };
        var writer  = new NotificationWriter(ctx, new NoOpBackgroundJobClient(), new FakeAuditPublisher(), new FakeCompanyNotificationSettingsReader());
        var handler = new NotifyOnLeaveRequestedHandler(
            writer, managerReader, new FakeEmployeeNameReader(), new FakeLogger<NotifyOnLeaveRequestedHandler>());

        var e = new LeaveRequestedIntegrationEvent(
            companyId, employeeId, requestId, Guid.NewGuid(),
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 3), 3m, Now);

        await handler.HandleAsync(e, CancellationToken.None);
        await handler.HandleAsync(e, CancellationToken.None);

        Assert.Single(ctx.Notifications);
    }

    private static NotificationsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NotificationsDbContext(options);
    }
}
