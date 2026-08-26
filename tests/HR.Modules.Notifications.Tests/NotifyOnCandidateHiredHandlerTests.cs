using HR.Modules.Notifications.Features.NotifyOnCandidateHired;
using HR.Modules.Notifications.Persistence;
using HR.Modules.Notifications.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Tests;

public class NotifyOnCandidateHiredHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Notifies_Manager_Keyed_On_CandidateId()
    {
        await using var ctx = BuildContext();
        var companyId    = Guid.NewGuid();
        var employeeId   = Guid.NewGuid();
        var managerId    = Guid.NewGuid();
        var candidateId  = Guid.NewGuid();
        var applicationId = Guid.NewGuid();

        var managerReader = new FakeManagerReader { ManagerId = managerId };
        var nameReader = new FakeEmployeeNameReader();
        nameReader.Names[employeeId] = "Taylor Smith";

        var writer  = new NotificationWriter(ctx, new NoOpBackgroundJobClient(), new FakeAuditPublisher(), new FakeCompanyNotificationSettingsReader());
        var handler = new NotifyOnCandidateHiredHandler(
            writer, managerReader, nameReader, new FakeLogger<NotifyOnCandidateHiredHandler>());

        await handler.HandleAsync(
            new CandidateHiredIntegrationEvent(companyId, applicationId, candidateId, employeeId, Guid.NewGuid(), Now),
            CancellationToken.None);

        var saved = await ctx.Notifications.SingleAsync();
        Assert.Equal(managerId, saved.EmployeeId);
        Assert.Equal(candidateId, saved.SourceEntityId);
        Assert.Equal(NotificationType.CandidateHired, saved.Type);
        Assert.Contains("Taylor Smith", saved.Title);
    }

    [Fact]
    public async Task HandleAsync_Skips_And_Logs_When_No_Manager()
    {
        await using var ctx = BuildContext();
        var managerReader = new FakeManagerReader { ManagerId = null };
        var logger = new FakeLogger<NotifyOnCandidateHiredHandler>();

        var writer  = new NotificationWriter(ctx, new NoOpBackgroundJobClient(), new FakeAuditPublisher(), new FakeCompanyNotificationSettingsReader());
        var handler = new NotifyOnCandidateHiredHandler(writer, managerReader, new FakeEmployeeNameReader(), logger);

        await handler.HandleAsync(
            new CandidateHiredIntegrationEvent(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now),
            CancellationToken.None);

        Assert.Empty(ctx.Notifications);
        Assert.Single(logger.Messages);
    }

    [Fact]
    public async Task HandleAsync_Is_Idempotent_Against_Redelivery()
    {
        await using var ctx = BuildContext();
        var managerId = Guid.NewGuid();
        var managerReader = new FakeManagerReader { ManagerId = managerId };

        var writer  = new NotificationWriter(ctx, new NoOpBackgroundJobClient(), new FakeAuditPublisher(), new FakeCompanyNotificationSettingsReader());
        var handler = new NotifyOnCandidateHiredHandler(
            writer, managerReader, new FakeEmployeeNameReader(), new FakeLogger<NotifyOnCandidateHiredHandler>());

        var e = new CandidateHiredIntegrationEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

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
