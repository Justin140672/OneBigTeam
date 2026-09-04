using HR.Infrastructure.Abstractions;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Jobs;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Sickness.Tests.Jobs;

/// <summary>
/// OBT-REM-04: the reminder job must be safe to retry. The Pending→Overdue transition is committed
/// per request before any notification/event, and the notify step is guarded by the durable
/// <see cref="INotificationWriter.ExistsAsync"/> key so a retry only fills gaps. A failure for one
/// employee (notification writer or event publisher) is logged and skipped without blocking the
/// rest of the batch; cancellation is never swallowed by the per-item catch.
/// </summary>
public class SicknessEvidenceReminderJobRetrySafetyTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 15, 2, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedUtcNow);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static SicknessDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<SicknessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static SicknessEvidenceReminderJob BuildJob(
        SicknessDbContext db, INotificationWriter writer, IIntegrationEventPublisher publisher) =>
        new(db, writer, publisher, new FakeClock(FixedUtcNow),
            NullLogger<SicknessEvidenceReminderJob>.Instance);

    // ── failure-injecting decorators ──────────────────────────────────────

    private sealed class ThrowingNotificationWriter(
        FakeNotificationWriter inner, Func<Guid, bool> failForEmployee, int failTimes = int.MaxValue)
        : INotificationWriter
    {
        private int _failures;
        public FakeNotificationWriter Inner => inner;

        public Task WriteAsync(Guid id, Guid companyId, Guid employeeId, string title, string? body,
            Guid sourceEntityId, NotificationType type, NotificationPriority priority,
            DateTimeOffset createdAt, CancellationToken cancellationToken = default)
        {
            if (failForEmployee(employeeId) && _failures < failTimes)
            {
                _failures++;
                throw new InvalidOperationException("notification store unavailable");
            }
            return inner.WriteAsync(id, companyId, employeeId, title, body, sourceEntityId, type, priority, createdAt, cancellationToken);
        }

        public Task<Result> WriteTemplatedAsync(Guid id, Guid companyId, Guid employeeId, NotificationType type,
            IReadOnlyDictionary<string, string> tokens, Guid sourceEntityId, NotificationPriority priority,
            DateTimeOffset createdAt, CancellationToken cancellationToken = default)
            => inner.WriteTemplatedAsync(id, companyId, employeeId, type, tokens, sourceEntityId, priority, createdAt, cancellationToken);

        public Task<bool> ExistsAsync(Guid employeeId, Guid sourceEntityId, NotificationType type, CancellationToken cancellationToken = default)
            => inner.ExistsAsync(employeeId, sourceEntityId, type, cancellationToken);

        public Task<DateTimeOffset?> GetLastSentAtAsync(Guid employeeId, Guid sourceEntityId, NotificationType type, CancellationToken cancellationToken = default)
            => inner.GetLastSentAtAsync(employeeId, sourceEntityId, type, cancellationToken);

        public Task<int> RemoveBySourceEntityAsync(Guid companyId, Guid sourceEntityId, NotificationType type, CancellationToken cancellationToken = default)
            => inner.RemoveBySourceEntityAsync(companyId, sourceEntityId, type, cancellationToken);
    }

    private sealed class ThrowingEventPublisher(Func<object, bool> fail, int failTimes = int.MaxValue) : IIntegrationEventPublisher
    {
        private int _failures;
        public List<object> Published { get; } = [];

        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
            where TEvent : IIntegrationEvent
        {
            if (fail(integrationEvent!) && _failures < failTimes)
            {
                _failures++;
                throw new InvalidOperationException("event bus unavailable");
            }
            Published.Add(integrationEvent!);
            return Task.CompletedTask;
        }
    }

    // ── seeding ──────────────────────────────────────────────────────────

    private static async Task<(Guid recordId, Guid employeeId, Guid companyId)> SeedRecordAsync(
        SicknessDbContext db, Guid? companyId = null)
    {
        var company = companyId ?? Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        db.SicknessCategories.Add(SicknessCategory.Create(categoryId, company, "Cold", 1, Now));
        var record = SicknessRecord.Create(
            Guid.NewGuid(), company, employeeId, categoryId, new DateOnly(2026, 6, 1),
            SicknessDayPart.FullDay, null, null, null, null, SicknessEvidenceStatus.Pending, Now);
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();
        return (record.Id, employeeId, company);
    }

    private static async Task<SicknessEvidenceRequest> SeedOverdueDueRequestAsync(
        SicknessDbContext db, Guid recordId, Guid companyId)
    {
        var request = SicknessEvidenceRequest.Create(
            Guid.NewGuid(), companyId, recordId, Guid.Empty, Today.AddDays(-1), null, Now);
        db.SicknessEvidenceRequests.Add(request);
        await db.SaveChangesAsync();
        return request;
    }

    // ── tests ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Status_transition_persists_even_when_the_overdue_notification_fails()
    {
        await using var db = BuildContext();
        var (recordId, employeeId, companyId) = await SeedRecordAsync(db);
        var request = await SeedOverdueDueRequestAsync(db, recordId, companyId);

        var fake = new FakeNotificationWriter();
        var writer = new ThrowingNotificationWriter(fake, _ => true, failTimes: 1);

        await BuildJob(db, writer, new FakeIntegrationEventPublisher()).ExecuteAsync();

        var updated = await db.SicknessEvidenceRequests.SingleAsync(r => r.Id == request.Id);
        Assert.Equal(SicknessEvidenceRequestStatus.Overdue, updated.Status);
        Assert.DoesNotContain(fake.Written, n => n.Type == NotificationType.SicknessEvidenceOverdue);

        // Retry: transition already done, notification now succeeds — sent exactly once.
        await BuildJob(db, writer, new FakeIntegrationEventPublisher()).ExecuteAsync();
        Assert.Single(fake.Written, n => n.Type == NotificationType.SicknessEvidenceOverdue);

        // A third run must not duplicate it (ExistsAsync guard).
        await BuildJob(db, writer, new FakeIntegrationEventPublisher()).ExecuteAsync();
        Assert.Single(fake.Written, n => n.Type == NotificationType.SicknessEvidenceOverdue);
    }

    [Fact]
    public async Task One_employees_notification_failure_does_not_block_the_others()
    {
        await using var db = BuildContext();
        var (recordA, empA, companyId) = await SeedRecordAsync(db);
        var (recordB, empB, _) = await SeedRecordAsync(db, companyId);
        await SeedOverdueDueRequestAsync(db, recordA, companyId);
        var reqB = await SeedOverdueDueRequestAsync(db, recordB, companyId);

        var fake = new FakeNotificationWriter();
        var writer = new ThrowingNotificationWriter(fake, emp => emp == empA);

        await BuildJob(db, writer, new FakeIntegrationEventPublisher()).ExecuteAsync();

        // Both transitioned; only B got its notification.
        var all = await db.SicknessEvidenceRequests.ToListAsync();
        Assert.All(all, r => Assert.Equal(SicknessEvidenceRequestStatus.Overdue, r.Status));
        var overdue = Assert.Single(fake.Written, n => n.Type == NotificationType.SicknessEvidenceOverdue);
        Assert.Equal(empB, overdue.EmployeeId);
        Assert.Equal(reqB.Id, overdue.SourceEntityId);
    }

    [Fact]
    public async Task One_employees_event_publish_failure_does_not_block_the_others()
    {
        await using var db = BuildContext();
        var (recordA, _, companyId) = await SeedRecordAsync(db);
        var (recordB, _, _) = await SeedRecordAsync(db, companyId);
        var reqA = await SeedOverdueDueRequestAsync(db, recordA, companyId);
        await SeedOverdueDueRequestAsync(db, recordB, companyId);

        var fake = new FakeNotificationWriter();
        var publisher = new ThrowingEventPublisher(
            e => e is SicknessEvidenceOverdueIntegrationEvent ev && ev.EvidenceRequestId == reqA.Id);

        await BuildJob(db, fake, publisher).ExecuteAsync();

        Assert.Single(publisher.Published.OfType<SicknessEvidenceOverdueIntegrationEvent>());
    }

    [Fact]
    public async Task Cancelled_token_stops_the_job_and_is_not_swallowed()
    {
        await using var db = BuildContext();
        var (recordId, _, companyId) = await SeedRecordAsync(db);
        await SeedOverdueDueRequestAsync(db, recordId, companyId);

        var fake = new FakeNotificationWriter();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => BuildJob(db, fake, new FakeIntegrationEventPublisher()).ExecuteAsync(cts.Token));

        Assert.Empty(fake.Written);
    }

    [Fact]
    public async Task Cross_company_overdue_requests_are_all_processed_and_scoped()
    {
        await using var db = BuildContext();
        var (recordA, empA, companyA) = await SeedRecordAsync(db);
        var (recordB, empB, companyB) = await SeedRecordAsync(db);
        await SeedOverdueDueRequestAsync(db, recordA, companyA);
        await SeedOverdueDueRequestAsync(db, recordB, companyB);

        var fake = new FakeNotificationWriter();
        await BuildJob(db, fake, new FakeIntegrationEventPublisher()).ExecuteAsync();

        var overdue = fake.Written.Where(n => n.Type == NotificationType.SicknessEvidenceOverdue).ToList();
        Assert.Equal(2, overdue.Count);
        Assert.Contains(overdue, n => n.CompanyId == companyA && n.EmployeeId == empA);
        Assert.Contains(overdue, n => n.CompanyId == companyB && n.EmployeeId == empB);
    }

    [Fact]
    public async Task Mixed_batch_of_due_soon_newly_overdue_and_already_overdue_is_handled()
    {
        await using var db = BuildContext();
        var (recordId, _, companyId) = await SeedRecordAsync(db);

        // due soon (reminder)
        db.SicknessEvidenceRequests.Add(SicknessEvidenceRequest.Create(
            Guid.NewGuid(), companyId, recordId, Guid.Empty, Today.AddDays(1), null, Now));
        // newly overdue
        db.SicknessEvidenceRequests.Add(SicknessEvidenceRequest.Create(
            Guid.NewGuid(), companyId, recordId, Guid.Empty, Today.AddDays(-1), null, Now));
        // already overdue (within reconciliation window)
        var already = SicknessEvidenceRequest.Create(
            Guid.NewGuid(), companyId, recordId, Guid.Empty, Today.AddDays(-4), null, Now);
        already.MarkOverdue(Now);
        db.SicknessEvidenceRequests.Add(already);
        await db.SaveChangesAsync();

        var fake = new FakeNotificationWriter();
        await BuildJob(db, fake, new FakeIntegrationEventPublisher()).ExecuteAsync();

        Assert.Single(fake.Written, n => n.Type == NotificationType.SicknessEvidenceReminder);
        // both the newly-overdue and the already-overdue (previously un-notified) get an overdue note
        Assert.Equal(2, fake.Written.Count(n => n.Type == NotificationType.SicknessEvidenceOverdue));
        Assert.Equal(2, await db.SicknessEvidenceRequests
            .CountAsync(r => r.Status == SicknessEvidenceRequestStatus.Overdue));
    }
}
