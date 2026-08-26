using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.UpdateNotificationSettings;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class UpdateNotificationSettingsHandlerTests
{
    private static UpdateNotificationSettingsRequest ValidRequest(Guid companyId) => new()
    {
        CompanyId = companyId,
        EmailNotificationsEnabled = false,
        ScheduledRemindersEnabled = false,
        Version = 1,
    };

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Company_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new UpdateNotificationSettingsHandler(
            context,
            new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)),
            new NoOpAuditEventPublisher(),
            new FakeCurrentUser(null));

        var result = await handler.HandleAsync(ValidRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Persists_New_Values_And_Bumps_Version_And_UpdatedAt()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
        var company = Company.Create(Guid.NewGuid(), "Acme", now);
        company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var updateTime = new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc);
        var handler = new UpdateNotificationSettingsHandler(
            context, new FakeClock(updateTime), new NoOpAuditEventPublisher(), new FakeCurrentUser(null));

        var result = await handler.HandleAsync(ValidRequest(company.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.EmailNotificationsEnabled);
        Assert.False(result.Value.ScheduledRemindersEnabled);
        Assert.Equal(new DateTimeOffset(updateTime, TimeSpan.Zero), result.Value.UpdatedAt);
        Assert.Equal(2, result.Value.Version);

        var savedSettings = await context.CompanySettings.SingleAsync();
        Assert.False(savedSettings.EmailNotificationsEnabled);
        Assert.False(savedSettings.ScheduledRemindersEnabled);
    }

    [Fact]
    public async Task HandleAsync_Publishes_NotificationSettingsUpdatedAuditEvent_With_Before_And_After_Snapshot()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
        var company = Company.Create(Guid.NewGuid(), "Acme", now);
        company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var auditPublisher = new CapturingAuditEventPublisher();
        var updateTime = new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc);
        var actorUserId = Guid.NewGuid();
        var handler = new UpdateNotificationSettingsHandler(
            context, new FakeClock(updateTime), auditPublisher, new FakeCurrentUser(actorUserId));

        await handler.HandleAsync(ValidRequest(company.Id), CancellationToken.None);

        var auditEvt = Assert.Single(auditPublisher.Published);
        var auditEvent = Assert.IsType<NotificationSettingsUpdatedAuditEvent>(auditEvt);
        Assert.Equal(company.Id, auditEvent.CompanyId);
        Assert.Equal(actorUserId, auditEvent.ActorUserId);

        Assert.NotNull(auditEvent.PreviousSettings);
        Assert.True(auditEvent.PreviousSettings!.EmailNotificationsEnabled);
        Assert.True(auditEvent.PreviousSettings.ScheduledRemindersEnabled);

        Assert.False(auditEvent.CurrentSettings.EmailNotificationsEnabled);
        Assert.False(auditEvent.CurrentSettings.ScheduledRemindersEnabled);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_And_Publishes_No_AuditEvent_When_Version_Is_Stale()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
        var company = Company.Create(Guid.NewGuid(), "Acme", now);
        company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var firstHandler = new UpdateNotificationSettingsHandler(
            context,
            new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)),
            new NoOpAuditEventPublisher(),
            new FakeCurrentUser(null));

        var firstResult = await firstHandler.HandleAsync(ValidRequest(company.Id) with { Version = 1 }, CancellationToken.None);
        Assert.True(firstResult.IsSuccess);
        Assert.Equal(2, firstResult.Value!.Version);

        var auditPublisher = new CapturingAuditEventPublisher();
        var secondHandler = new UpdateNotificationSettingsHandler(
            context,
            new FakeClock(new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc)),
            auditPublisher,
            new FakeCurrentUser(null));

        var secondResult = await secondHandler.HandleAsync(ValidRequest(company.Id) with { Version = 1 }, CancellationToken.None);

        Assert.True(secondResult.IsFailure);
        Assert.Equal("conflict", secondResult.Error.Code);
        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Creates_Default_Settings_Then_Updates_When_Company_Has_No_Existing_Settings_Row()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
        var company = Company.Create(Guid.NewGuid(), "Acme", now);
        // Deliberately no SetSettings() call — company.Settings is null.
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = new UpdateNotificationSettingsHandler(
            context,
            new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)),
            new NoOpAuditEventPublisher(),
            new FakeCurrentUser(null));

        var result = await handler.HandleAsync(ValidRequest(company.Id) with { Version = 1 }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.EmailNotificationsEnabled);
        Assert.False(result.Value.ScheduledRemindersEnabled);

        var savedSettings = await context.CompanySettings.SingleAsync();
        Assert.Equal(company.Id, savedSettings.CompanyId);
        Assert.False(savedSettings.EmailNotificationsEnabled);
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}
