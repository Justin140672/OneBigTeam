using HR.Modules.Employees.Contracts;
using HR.Modules.Notifications.Features.NotifyOnEmployeeCreated;
using HR.Modules.Notifications.Persistence;
using HR.Modules.Notifications.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Tests;

public class NotifyOnEmployeeCreatedHandlerTests
{
    [Fact]
    public async Task HandleAsync_Notifies_Manager_When_Present()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId  = Guid.NewGuid();

        var nameReader = new FakeEmployeeNameReader();
        nameReader.Names[employeeId] = "Jamie Lee";

        var handler = BuildHandler(ctx, nameReader, new FakePositionProfileReader(), new FakeHrAdministratorDirectory());

        await handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(
                companyId, employeeId, DateOnly.FromDateTime(DateTime.Today), managerId,
                DateOnly.FromDateTime(DateTime.Today.AddMonths(6))),
            CancellationToken.None);

        var saved = await ctx.Notifications.SingleAsync();
        Assert.Equal(managerId, saved.EmployeeId);
        Assert.Contains("Jamie Lee", saved.Title);
    }

    [Fact]
    public async Task HandleAsync_Falls_Back_To_Hr_Administrators_When_No_Manager()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var hrAdminId  = Guid.NewGuid();

        var hrDirectory = new FakeHrAdministratorDirectory { HrAdministratorEmployeeIds = [hrAdminId] };
        var handler = BuildHandler(ctx, new FakeEmployeeNameReader(), new FakePositionProfileReader(), hrDirectory);

        await handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(
                companyId, employeeId, DateOnly.FromDateTime(DateTime.Today), null,
                DateOnly.FromDateTime(DateTime.Today.AddMonths(6))),
            CancellationToken.None);

        var saved = await ctx.Notifications.SingleAsync();
        Assert.Equal(hrAdminId, saved.EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Skips_And_Logs_When_No_Manager_And_No_Hr_Administrators()
    {
        await using var ctx = BuildContext();
        var logger = new FakeLogger<NotifyOnEmployeeCreatedHandler>();
        var handler = BuildHandler(
            ctx, new FakeEmployeeNameReader(), new FakePositionProfileReader(), new FakeHrAdministratorDirectory(), logger);

        await handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(
                Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today), null,
                DateOnly.FromDateTime(DateTime.Today.AddMonths(6))),
            CancellationToken.None);

        Assert.Empty(ctx.Notifications);
        Assert.Single(logger.Messages);
    }

    [Fact]
    public async Task HandleAsync_Skips_Imported_Employees()
    {
        await using var ctx = BuildContext();
        var managerId = Guid.NewGuid();
        var handler = BuildHandler(ctx, new FakeEmployeeNameReader(), new FakePositionProfileReader(), new FakeHrAdministratorDirectory());

        await handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(
                Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today), managerId,
                DateOnly.FromDateTime(DateTime.Today.AddMonths(6)), IsImported: true),
            CancellationToken.None);

        Assert.Empty(ctx.Notifications);
    }

    [Fact]
    public async Task HandleAsync_Is_Idempotent_Against_Redelivery()
    {
        await using var ctx = BuildContext();
        var managerId = Guid.NewGuid();
        var handler = BuildHandler(ctx, new FakeEmployeeNameReader(), new FakePositionProfileReader(), new FakeHrAdministratorDirectory());

        var e = new EmployeeCreatedIntegrationEvent(
            Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today), managerId,
            DateOnly.FromDateTime(DateTime.Today.AddMonths(6)));

        await handler.HandleAsync(e, CancellationToken.None);
        await handler.HandleAsync(e, CancellationToken.None);

        Assert.Single(ctx.Notifications);
    }

    private static NotifyOnEmployeeCreatedHandler BuildHandler(
        NotificationsDbContext ctx,
        FakeEmployeeNameReader nameReader,
        FakePositionProfileReader positionProfileReader,
        FakeHrAdministratorDirectory hrDirectory,
        FakeLogger<NotifyOnEmployeeCreatedHandler>? logger = null) =>
        new(
            new NotificationWriter(ctx, new NoOpBackgroundJobClient(), new FakeAuditPublisher(), new FakeCompanyNotificationSettingsReader()),
            nameReader,
            positionProfileReader,
            hrDirectory,
            new FakeClock(new DateTime(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc)),
            logger ?? new FakeLogger<NotifyOnEmployeeCreatedHandler>());

    private static NotificationsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NotificationsDbContext(options);
    }
}
