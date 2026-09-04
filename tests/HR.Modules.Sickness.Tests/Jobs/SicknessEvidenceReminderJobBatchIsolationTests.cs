using HR.Infrastructure.Abstractions;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Jobs;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Sickness.Tests.Jobs;

/// <summary>
/// OBT-REM-10: a <c>SaveChangesAsync</c> failure while transitioning one request in a batch from
/// Pending to Overdue must only affect that one request. Before this fix,
/// <c>db.ChangeTracker.Clear()</c> on a save failure detached every other request already loaded
/// into the batch, silently preventing their transitions from being persisted.
/// </summary>
public class SicknessEvidenceReminderJobBatchIsolationTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 15, 2, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedUtcNow);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static DbContextOptions<SicknessDbContext> BuildOptions() =>
        new DbContextOptionsBuilder<SicknessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static SicknessEvidenceReminderJob BuildJob(
        SicknessDbContext db, INotificationWriter writer, IIntegrationEventPublisher publisher) =>
        new(db, writer, publisher, new FakeClock(FixedUtcNow),
            NullLogger<SicknessEvidenceReminderJob>.Instance);

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

    /// <summary>Fails the very next SaveChangesAsync call whose modified SicknessEvidenceRequest set
    /// includes the given id — exactly once — regardless of what order the job's batch loop visits
    /// requests in.</summary>
    private static Func<SicknessDbContext, bool> FailOnceForRequest(Guid requestId)
    {
        var alreadyFailed = false;
        return db =>
        {
            if (alreadyFailed) return false;

            var isTargeted = db.ChangeTracker.Entries<SicknessEvidenceRequest>()
                .Any(e => e.State == EntityState.Modified && e.Entity.Id == requestId);

            if (!isTargeted) return false;

            alreadyFailed = true;
            return true;
        };
    }

    [Fact]
    public async Task Save_failure_on_the_first_batch_item_does_not_block_the_second_items_transition()
    {
        var options = BuildOptions();
        await using var seedDb = new SicknessDbContext(options);
        var (recordA, _, companyId) = await SeedRecordAsync(seedDb);
        var (recordB, _, _) = await SeedRecordAsync(seedDb, companyId);
        var reqA = await SeedOverdueDueRequestAsync(seedDb, recordA, companyId);
        var reqB = await SeedOverdueDueRequestAsync(seedDb, recordB, companyId);

        await using var db = new FailingSaveSicknessDbContext(options, FailOnceForRequest(reqA.Id));
        var job = BuildJob(db, new FakeNotificationWriter(), new FakeIntegrationEventPublisher());

        await job.ExecuteAsync();

        await using var verifyDb = new SicknessDbContext(options);
        var updatedA = await verifyDb.SicknessEvidenceRequests.SingleAsync(r => r.Id == reqA.Id);
        var updatedB = await verifyDb.SicknessEvidenceRequests.SingleAsync(r => r.Id == reqB.Id);

        Assert.Equal(SicknessEvidenceRequestStatus.Pending, updatedA.Status);
        Assert.Equal(SicknessEvidenceRequestStatus.Overdue, updatedB.Status);
    }

    [Fact]
    public async Task Save_failure_on_a_middle_batch_item_does_not_block_earlier_or_later_items()
    {
        var options = BuildOptions();
        await using var seedDb = new SicknessDbContext(options);
        var (recordA, _, companyId) = await SeedRecordAsync(seedDb);
        var (recordB, _, _) = await SeedRecordAsync(seedDb, companyId);
        var (recordC, _, _) = await SeedRecordAsync(seedDb, companyId);
        var reqA = await SeedOverdueDueRequestAsync(seedDb, recordA, companyId);
        var reqB = await SeedOverdueDueRequestAsync(seedDb, recordB, companyId);
        var reqC = await SeedOverdueDueRequestAsync(seedDb, recordC, companyId);

        // Target the "middle" one, whichever entity it turns out to be after the seed order —
        // the important assertion is that the other two are unaffected either way.
        await using var db = new FailingSaveSicknessDbContext(options, FailOnceForRequest(reqB.Id));
        var job = BuildJob(db, new FakeNotificationWriter(), new FakeIntegrationEventPublisher());

        await job.ExecuteAsync();

        await using var verifyDb = new SicknessDbContext(options);
        var updatedA = await verifyDb.SicknessEvidenceRequests.SingleAsync(r => r.Id == reqA.Id);
        var updatedB = await verifyDb.SicknessEvidenceRequests.SingleAsync(r => r.Id == reqB.Id);
        var updatedC = await verifyDb.SicknessEvidenceRequests.SingleAsync(r => r.Id == reqC.Id);

        Assert.Equal(SicknessEvidenceRequestStatus.Overdue, updatedA.Status);
        Assert.Equal(SicknessEvidenceRequestStatus.Pending, updatedB.Status);
        Assert.Equal(SicknessEvidenceRequestStatus.Overdue, updatedC.Status);
    }

    [Fact]
    public async Task Save_failure_on_the_last_batch_item_does_not_block_earlier_items()
    {
        var options = BuildOptions();
        await using var seedDb = new SicknessDbContext(options);
        var (recordA, _, companyId) = await SeedRecordAsync(seedDb);
        var (recordB, _, _) = await SeedRecordAsync(seedDb, companyId);
        var reqA = await SeedOverdueDueRequestAsync(seedDb, recordA, companyId);
        var reqB = await SeedOverdueDueRequestAsync(seedDb, recordB, companyId);

        await using var db = new FailingSaveSicknessDbContext(options, FailOnceForRequest(reqB.Id));
        var job = BuildJob(db, new FakeNotificationWriter(), new FakeIntegrationEventPublisher());

        await job.ExecuteAsync();

        await using var verifyDb = new SicknessDbContext(options);
        var updatedA = await verifyDb.SicknessEvidenceRequests.SingleAsync(r => r.Id == reqA.Id);
        var updatedB = await verifyDb.SicknessEvidenceRequests.SingleAsync(r => r.Id == reqB.Id);

        Assert.Equal(SicknessEvidenceRequestStatus.Overdue, updatedA.Status);
        Assert.Equal(SicknessEvidenceRequestStatus.Pending, updatedB.Status);
    }

    [Fact]
    public async Task Multiple_companies_are_processed_and_a_failure_in_one_company_does_not_affect_another()
    {
        var options = BuildOptions();
        await using var seedDb = new SicknessDbContext(options);
        var (recordA, empA, companyA) = await SeedRecordAsync(seedDb);
        var (recordB, empB, companyB) = await SeedRecordAsync(seedDb);
        var reqA = await SeedOverdueDueRequestAsync(seedDb, recordA, companyA);
        var reqB = await SeedOverdueDueRequestAsync(seedDb, recordB, companyB);

        await using var db = new FailingSaveSicknessDbContext(options, FailOnceForRequest(reqA.Id));
        var writer = new FakeNotificationWriter();
        var events = new FakeIntegrationEventPublisher();
        var job = BuildJob(db, writer, events);

        await job.ExecuteAsync();

        await using var verifyDb = new SicknessDbContext(options);
        var updatedA = await verifyDb.SicknessEvidenceRequests.SingleAsync(r => r.Id == reqA.Id);
        var updatedB = await verifyDb.SicknessEvidenceRequests.SingleAsync(r => r.Id == reqB.Id);

        Assert.Equal(SicknessEvidenceRequestStatus.Pending, updatedA.Status);
        Assert.Equal(SicknessEvidenceRequestStatus.Overdue, updatedB.Status);

        var overdueNotification = Assert.Single(writer.Written, n => n.Type == NotificationType.SicknessEvidenceOverdue);
        Assert.Equal(companyB, overdueNotification.CompanyId);
        Assert.Equal(empB, overdueNotification.EmployeeId);

        var overdueEvent = Assert.Single(events.PublishedEvents.OfType<SicknessEvidenceOverdueIntegrationEvent>());
        Assert.Equal(companyB, overdueEvent.CompanyId);
        Assert.Equal(empB, overdueEvent.EmployeeId);
    }

    [Fact]
    public async Task Two_fully_successful_runs_in_a_row_produce_no_duplicate_notifications_or_events()
    {
        var options = BuildOptions();
        await using var seedDb = new SicknessDbContext(options);
        var (recordId, _, companyId) = await SeedRecordAsync(seedDb);
        await SeedOverdueDueRequestAsync(seedDb, recordId, companyId);

        var writer = new FakeNotificationWriter();
        var events = new FakeIntegrationEventPublisher();

        await using (var db = new SicknessDbContext(options))
        {
            await BuildJob(db, writer, events).ExecuteAsync();
        }

        await using (var db = new SicknessDbContext(options))
        {
            await BuildJob(db, writer, events).ExecuteAsync();
        }

        Assert.Single(writer.Written, n => n.Type == NotificationType.SicknessEvidenceOverdue);
        Assert.Single(events.PublishedEvents.OfType<SicknessEvidenceOverdueIntegrationEvent>());
    }
}
