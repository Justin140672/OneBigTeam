using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Jobs;
using HR.Modules.Notifications.Persistence;
using HR.Modules.Notifications.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
        AdministrativeAlertCategory category = AdministrativeAlertCategory.IntegrationDelivery,
        DateTimeOffset? occurredAt = null,
        string? actionUrl = null,
        int? affectedItemCount = null,
        AdministrativeAlertReason? reason = null) =>
        new(
            companyId,
            severity,
            category,
            "Delivery failing",
            "detail",
            occurredAt ?? new DateTimeOffset(FixedUtcNow),
            dedupKey,
            "EmailDelivery",
            Guid.NewGuid(),
            "check",
            actionUrl,
            affectedItemCount,
            reason);

    private static AdministrativeAlertWriter BuildWriter(
        NotificationsDbContext db,
        out FakeAuditPublisher audit,
        out RecordingBackgroundJobClient jobs,
        OperationalAlertEmailOptions? options = null)
    {
        audit = new FakeAuditPublisher();
        jobs = new RecordingBackgroundJobClient();
        return new AdministrativeAlertWriter(
            db,
            audit,
            jobs,
            Options.Create(options ?? new OperationalAlertEmailOptions()),
            new FakeClock(FixedUtcNow));
    }

    private static IReadOnlyList<Guid> EnqueuedAlertIds(RecordingBackgroundJobClient jobs) =>
        jobs.CreatedJobs
            .Where(j => j.Type == typeof(SendOperationalAlertEmailJob))
            .Select(j => (Guid)j.Args[0]!)
            .ToList();

    // ---- existing behaviour (unchanged) -------------------------------------------------------

    [Fact]
    public async Task First_Raise_Creates_One_Row_And_Publishes_NonRecurrence_Audit()
    {
        await using var db = BuildContext();
        var writer = BuildWriter(db, out var audit, out _);
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
        var writer = BuildWriter(db, out var audit, out _);
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
        var writer = BuildWriter(db, out _, out _);
        var companyId = Guid.NewGuid();

        await writer.RaiseAsync(Command(companyId, dedupKey: "a"));
        await writer.RaiseAsync(Command(companyId, dedupKey: "b"));

        Assert.Equal(2, await db.AdministrativeAlerts.CountAsync());
    }

    [Fact]
    public async Task Same_DedupKey_Different_Company_Creates_A_Second_Row()
    {
        await using var db = BuildContext();
        var writer = BuildWriter(db, out _, out _);

        await writer.RaiseAsync(Command(Guid.NewGuid()));
        await writer.RaiseAsync(Command(Guid.NewGuid()));

        Assert.Equal(2, await db.AdministrativeAlerts.CountAsync());
    }

    [Fact]
    public async Task After_Resolution_A_Fresh_Raise_With_The_Same_DedupKey_Starts_A_New_Row()
    {
        await using var db = BuildContext();
        var writer = BuildWriter(db, out _, out _);
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
        var writer = BuildWriter(db, out _, out _);
        var companyId = Guid.NewGuid();

        await writer.RaiseAsync(Command(companyId, severity: AdministrativeAlertSeverity.Warning));
        await writer.RaiseAsync(Command(companyId, severity: AdministrativeAlertSeverity.Critical));
        await writer.RaiseAsync(Command(companyId, severity: AdministrativeAlertSeverity.Info));

        var row = Assert.Single(db.AdministrativeAlerts);
        Assert.Equal(AdministrativeAlertSeverity.Critical, row.Severity);
    }

    // ---- Follow-up F: operations-email is gated on Reason == MissingDocumentExport -----------
    // A new alert queues the internal-operations notification email ONLY when the command carries
    // Reason == MissingDocumentExport. The broad Category (ReportGeneration etc.) no longer triggers
    // the email on its own — an ordinary report-export failure carries no Reason and sends nothing.

    private static RaiseAdministrativeAlertCommand MissingDocumentCommand(
        Guid companyId,
        string dedupKey = "integration:email-delivery-failure",
        int? affectedItemCount = null) =>
        Command(
            companyId,
            dedupKey: dedupKey,
            category: AdministrativeAlertCategory.ReportGeneration,
            affectedItemCount: affectedItemCount,
            reason: AdministrativeAlertReason.MissingDocumentExport);

    // (a) one missing-document failure queues exactly one notification
    [Fact]
    public async Task New_MissingDocument_Alert_Creates_One_Pending_Delivery_Row_And_Enqueues_Send_Job()
    {
        await using var db = BuildContext();
        var writer = BuildWriter(db, out _, out var jobs);
        var companyId = Guid.NewGuid();

        await writer.RaiseAsync(MissingDocumentCommand(companyId));

        var alert = Assert.Single(db.AdministrativeAlerts);
        Assert.Equal(AdministrativeAlertReason.MissingDocumentExport, alert.Reason);
        var delivery = Assert.Single(db.OperationalAlertEmailDeliveries);
        Assert.Equal(alert.Id, delivery.AlertId);
        Assert.Equal(companyId, delivery.CompanyId);
        Assert.Equal(EmailDeliveryStatus.Pending, delivery.Status);
        Assert.Equal(0, delivery.AttemptCount);

        Assert.Equal(new[] { alert.Id }, EnqueuedAlertIds(jobs));
    }

    // (b) an ordinary report-export failure (ReportGeneration, no reason) queues none
    [Fact]
    public async Task New_ReportGeneration_Alert_Without_A_Reason_Creates_No_Delivery_Row_And_Enqueues_Nothing()
    {
        await using var db = BuildContext();
        var writer = BuildWriter(db, out _, out var jobs);

        await writer.RaiseAsync(Command(
            Guid.NewGuid(), category: AdministrativeAlertCategory.ReportGeneration, reason: null));

        var alert = Assert.Single(db.AdministrativeAlerts);
        Assert.Null(alert.Reason);
        Assert.Empty(db.OperationalAlertEmailDeliveries);
        Assert.Empty(EnqueuedAlertIds(jobs));
    }

    [Fact]
    public async Task New_NonReportGeneration_Alert_Creates_No_Delivery_Row_And_Enqueues_Nothing()
    {
        await using var db = BuildContext();
        var writer = BuildWriter(db, out _, out var jobs);

        await writer.RaiseAsync(Command(Guid.NewGuid(), category: AdministrativeAlertCategory.IntegrationDelivery));

        Assert.Empty(db.OperationalAlertEmailDeliveries);
        Assert.Empty(EnqueuedAlertIds(jobs));
    }

    // (c) repeated missing-document failures while the alert is open queue no additional notifications
    [Fact]
    public async Task Recurrence_Of_An_Open_MissingDocument_Alert_Adds_No_Delivery_Row_And_No_Extra_Enqueue()
    {
        await using var db = BuildContext();
        var writer = BuildWriter(db, out _, out var jobs);
        var companyId = Guid.NewGuid();

        await writer.RaiseAsync(MissingDocumentCommand(companyId, affectedItemCount: 2));
        await writer.RaiseAsync(MissingDocumentCommand(companyId, affectedItemCount: 5));

        var alert = Assert.Single(db.AdministrativeAlerts);
        Assert.Equal(2, alert.OccurrenceCount);
        Assert.Equal(5, alert.AffectedItemCount);
        Assert.Single(db.OperationalAlertEmailDeliveries);
        Assert.Single(EnqueuedAlertIds(jobs));
    }

    // (d) a missing-document failure after resolution queues a new notification
    [Fact]
    public async Task Identical_MissingDocument_Failure_After_Resolution_Opens_New_Alert_With_New_Delivery_And_New_Enqueue()
    {
        await using var db = BuildContext();
        var writer = BuildWriter(db, out _, out var jobs);
        var companyId = Guid.NewGuid();

        await writer.RaiseAsync(MissingDocumentCommand(companyId));
        var first = await db.AdministrativeAlerts.SingleAsync();
        first.Resolve(Guid.NewGuid(), null, new DateTimeOffset(FixedUtcNow));
        await db.SaveChangesAsync();

        await writer.RaiseAsync(MissingDocumentCommand(companyId));

        Assert.Equal(2, await db.AdministrativeAlerts.CountAsync());
        Assert.Equal(2, await db.OperationalAlertEmailDeliveries.CountAsync());
        var openAlert = await db.AdministrativeAlerts.SingleAsync(a => a.Status != AdministrativeAlertStatus.Resolved);
        var enqueued = EnqueuedAlertIds(jobs);
        Assert.Equal(2, enqueued.Count);
        Assert.Contains(openAlert.Id, enqueued);
    }

    // ---- Follow-up C: ActionUrl enrichment --------------------------------------------------

    [Fact]
    public async Task ActionUrl_Is_Enriched_From_AdminAppBaseUrl_When_Command_Supplies_None()
    {
        await using var db = BuildContext();
        var writer = BuildWriter(db, out _, out _,
            new OperationalAlertEmailOptions { AdminAppBaseUrl = "https://admin.example/" });
        var companyId = Guid.NewGuid();

        await writer.RaiseAsync(Command(companyId, category: AdministrativeAlertCategory.ReportGeneration, actionUrl: null));

        var alert = Assert.Single(db.AdministrativeAlerts);
        Assert.Equal($"https://admin.example/operational-alerts/{alert.Id}", alert.ActionUrl);
    }

    [Fact]
    public async Task ActionUrl_Stays_Null_When_No_AdminAppBaseUrl_Configured()
    {
        await using var db = BuildContext();
        var writer = BuildWriter(db, out _, out _, new OperationalAlertEmailOptions());

        await writer.RaiseAsync(Command(Guid.NewGuid(), actionUrl: null));

        var alert = Assert.Single(db.AdministrativeAlerts);
        Assert.Null(alert.ActionUrl);
    }

    [Fact]
    public async Task ActionUrl_Supplied_By_Command_Is_Left_As_Is()
    {
        await using var db = BuildContext();
        var writer = BuildWriter(db, out _, out _,
            new OperationalAlertEmailOptions { AdminAppBaseUrl = "https://admin.example" });

        await writer.RaiseAsync(Command(Guid.NewGuid(), actionUrl: "https://caller.example/custom"));

        var alert = Assert.Single(db.AdministrativeAlerts);
        Assert.Equal("https://caller.example/custom", alert.ActionUrl);
    }

    // NOTE: the DbUpdateException race-loser path (writer folds a lost partial-unique-index race into
    // a recurrence and enqueues no email) cannot be exercised here — the EF Core InMemory provider
    // does not enforce the filtered unique (company_id, dedup_key) index, so SaveChanges never throws.
    // That path is covered by the concurrent-creation case in
    // HR.Integration.Tests/OperationalAlertEmailNotificationTests against real Postgres.
}
