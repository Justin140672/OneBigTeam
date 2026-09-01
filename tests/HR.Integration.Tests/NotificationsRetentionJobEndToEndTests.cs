using HR.Infrastructure.Persistence;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Notifications.Jobs;
using HR.Modules.Notifications.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// NFR-07: end-to-end wiring for the notifications retention sweep — resolves the job from the real
/// DI container and runs it against the real DbContexts. Detailed branch coverage lives in
/// HR.Modules.Notifications.Tests/PurgeExpiredReadNotificationsJobTests.
/// </summary>
[Collection("Integration")]
public class NotificationsRetentionJobEndToEndTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly DateTimeOffset Old = DateTimeOffset.UtcNow.AddDays(-400);
    private static readonly DateTimeOffset Recent = DateTimeOffset.UtcNow.AddDays(-5);

    public NotificationsRetentionJobEndToEndTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task PlaceLegalHoldAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        // Creates the Company + CustomerSubscription rows (customer_subscriptions FKs to companies).
        await TestRoleSeeder.EnsureActiveSubscriptionAsync(scope, companyId);

        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var subscription = await db.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        subscription.PlaceLegalHold(Guid.NewGuid(), "Litigation hold for retention e2e test", DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Live_Run_Deletes_Only_Old_Read_Notifications_Of_NonHeld_Company()
    {
        var freeCompany = Guid.NewGuid();
        var heldCompany = Guid.NewGuid();
        var employee = Guid.NewGuid();

        var freeOldRead = await NotificationSeeder.SeedAsync(_factory, freeCompany, employee, "free old read", isRead: true, createdAt: Old);
        var freeNewRead = await NotificationSeeder.SeedAsync(_factory, freeCompany, employee, "free new read", isRead: true, createdAt: Recent);
        var freeOldUnread = await NotificationSeeder.SeedAsync(_factory, freeCompany, employee, "free old unread", isRead: false, createdAt: Old);
        var heldOldRead = await NotificationSeeder.SeedAsync(_factory, heldCompany, employee, "held old read", isRead: true, createdAt: Old);

        await PlaceLegalHoldAsync(heldCompany);

        using var scope = _factory.Services.CreateScope();
        var job = ActivatorUtilities.CreateInstance<PurgeExpiredReadNotificationsJob>(
            scope.ServiceProvider,
            BuildRetentionConfiguration(enabled: true));
        await job.ExecuteAsync(CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        Assert.False(await db.Notifications.AnyAsync(n => n.Id == freeOldRead));
        Assert.True(await db.Notifications.AnyAsync(n => n.Id == freeNewRead));
        Assert.True(await db.Notifications.AnyAsync(n => n.Id == freeOldUnread));
        Assert.True(await db.Notifications.AnyAsync(n => n.Id == heldOldRead));

        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var freeAudit = await auditDb.AuditEvents
            .Where(e => e.CompanyId == freeCompany && e.EventType == "notifications.retention-run")
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();
        Assert.NotNull(freeAudit);
        Assert.DoesNotContain("free old read", freeAudit!.Summary ?? string.Empty);

        var heldAudit = await auditDb.AuditEvents
            .Where(e => e.CompanyId == heldCompany && e.EventType == "notifications.retention-run")
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();
        Assert.NotNull(heldAudit);
    }

    [Fact]
    public async Task DryRun_Deletes_Nothing()
    {
        var company = Guid.NewGuid();
        var employee = Guid.NewGuid();
        var oldRead = await NotificationSeeder.SeedAsync(_factory, company, employee, "dry run old read", isRead: true, createdAt: Old);

        using var scope = _factory.Services.CreateScope();
        var job = ActivatorUtilities.CreateInstance<PurgeExpiredReadNotificationsJob>(
            scope.ServiceProvider,
            BuildRetentionConfiguration(enabled: false));
        await job.ExecuteAsync(CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        Assert.True(await db.Notifications.AnyAsync(n => n.Id == oldRead));
    }

    private static IConfiguration BuildRetentionConfiguration(bool enabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:Retention:Enabled"] = enabled ? "true" : "false",
                ["Notifications:Retention:RetentionDays"] = "365",
            })
            .Build();
}
