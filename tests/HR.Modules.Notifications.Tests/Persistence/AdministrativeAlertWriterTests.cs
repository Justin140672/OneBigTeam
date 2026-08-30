using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications;
using HR.Modules.Notifications.Persistence;
using HR.Modules.Notifications.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Tests.Persistence;

public class AdministrativeAlertWriterTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 8, 30, 10, 0, 0, DateTimeKind.Utc);

    private static NotificationsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static RaiseAdministrativeAlertCommand Command(
        Guid companyId,
        string dedupKey = "integration:email-delivery-failure",
        AdministrativeAlertSeverity severity = AdministrativeAlertSeverity.Warning,
        DateTimeOffset? occurredAt = null) =>
        new(
            companyId,
            severity,
            AdministrativeAlertCategory.IntegrationDelivery,
            "Delivery failing",
            "detail",
            occurredAt ?? new DateTimeOffset(FixedUtcNow),
            dedupKey,
            "EmailDelivery",
            Guid.NewGuid(),
            "check",
            null);

    private static AdministrativeAlertWriter BuildWriter(
        NotificationsDbContext db, out FakeAuditPublisher audit)
    {
        audit = new FakeAuditPublisher();
        return new AdministrativeAlertWriter(db, audit, new FakeClock(FixedUtcNow));
    }

    [Fact]
    public async Task First_Raise_Creates_One_Row_And_Publishes_NonRecurrence_Audit()
    {
        await using var db = BuildContext();
        var writer = BuildWriter(db, out var audit);
        var companyId = Guid.NewGuid();

        await writer.RaiseAsync(Command(companyId));

        var row = Assert.Single(db.AdministrativeAlerts);
        Assert.Equal(1, row.OccurrenceCount);
        var evt = Assert.IsType<AdministrativeAlertRaisedAuditEvent>(Assert.Single(audit.Published));
        Assert.False(evt.IsRecurrence);
    }

    [Fact]
    public async Task Second_Raise_Same_Company_And_DedupKey_While_Open_Folds_Into_The_Same_Row()
    {
        await using var db = BuildContext();
        var writer = BuildWriter(db, out var audit);
        var companyId = Guid.NewGuid();

        await writer.RaiseAsync(Command(companyId, occurredAt: new DateTimeOffset(FixedUtcNow)));
        await writer.RaiseAsync(Command(companyId, occurredAt: new DateTimeOffset(FixedUtcNow).AddHours(2)));

        var row = Assert.Single(db.AdministrativeAlerts);
        Assert.Equal(2, row.OccurrenceCount);
        Assert.Equal(new DateTimeOffset(FixedUtcNow).AddHours(2), row.LastOccurredAt);

        Assert.Equal(2, audit.Published.Count);
        var recurrence = Assert.IsType<AdministrativeAlertRaisedAuditEvent>(audit.Published[1]);
        Assert.True(recurrence.IsRecurrence);
    }

    [Fact]
    public async Task Different_DedupKey_Creates_A_Second_Row()
    {
        await using var db = BuildContext();
        var writer = BuildWriter(db, out _);
        var companyId = Guid.NewGuid();

        await writer.RaiseAsync(Command(companyId, dedupKey: "a"));
        await writer.RaiseAsync(Command(companyId, dedupKey: "b"));

        Assert.Equal(2, await db.AdministrativeAlerts.CountAsync());
    }

    [Fact]
    public async Task Same_DedupKey_Different_Company_Creates_A_Second_Row()
    {
        await using var db = BuildContext();
        var writer = BuildWriter(db, out _);

        await writer.RaiseAsync(Command(Guid.NewGuid()));
        await writer.RaiseAsync(Command(Guid.NewGuid()));

        Assert.Equal(2, await db.AdministrativeAlerts.CountAsync());
    }

    [Fact]
    public async Task After_Resolution_A_Fresh_Raise_With_The_Same_DedupKey_Starts_A_New_Row()
    {
        await using var db = BuildContext();
        var writer = BuildWriter(db, out _);
        var companyId = Guid.NewGuid();

        await writer.RaiseAsync(Command(companyId));
        var first = await db.AdministrativeAlerts.SingleAsync();
        first.Resolve(Guid.NewGuid(), null, new DateTimeOffset(FixedUtcNow));
        await db.SaveChangesAsync();

        await writer.RaiseAsync(Command(companyId));

        Assert.Equal(2, await db.AdministrativeAlerts.CountAsync());
        Assert.Equal(1, await db.AdministrativeAlerts.CountAsync(a => a.Status != AdministrativeAlertStatus.Resolved));
    }

    [Fact]
    public async Task Recurrence_Escalates_Severity_Upward_Only()
    {
        await using var db = BuildContext();
        var writer = BuildWriter(db, out _);
        var companyId = Guid.NewGuid();

        await writer.RaiseAsync(Command(companyId, severity: AdministrativeAlertSeverity.Warning));
        await writer.RaiseAsync(Command(companyId, severity: AdministrativeAlertSeverity.Critical));
        await writer.RaiseAsync(Command(companyId, severity: AdministrativeAlertSeverity.Info));

        var row = Assert.Single(db.AdministrativeAlerts);
        Assert.Equal(AdministrativeAlertSeverity.Critical, row.Severity);
    }
}
