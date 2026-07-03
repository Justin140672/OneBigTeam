using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Jobs;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests.Jobs;

public class SicknessEvidenceReminderJobTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 15, 2, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedUtcNow);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static SicknessDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<SicknessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static SicknessEvidenceReminderJob BuildJob(
        SicknessDbContext db,
        FakeNotificationWriter writer,
        FakeIntegrationEventPublisher? eventPublisher = null) =>
        new(db, writer, eventPublisher ?? new FakeIntegrationEventPublisher(), new FakeClock(FixedUtcNow));

    private static async Task<(Guid recordId, Guid employeeId, Guid companyId)> SeedRecordAsync(
        SicknessDbContext db)
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        var category = SicknessCategory.Create(categoryId, companyId, "Cold", 1, Now);
        db.SicknessCategories.Add(category);

        var record = SicknessRecord.Create(
            Guid.NewGuid(),
            companyId,
            employeeId,
            categoryId,
            new DateOnly(2026, 6, 1),
            SicknessDayPart.FullDay,
            endDate: null,
            endDayPart: null,
            totalDays: null,
            notes: null,
            evidenceStatus: SicknessEvidenceStatus.Pending,
            now: Now);
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();

        return (record.Id, employeeId, companyId);
    }

    private static async Task<SicknessEvidenceRequest> SeedRequestAsync(
        SicknessDbContext db,
        Guid recordId,
        Guid companyId,
        DateOnly dueDate,
        SicknessEvidenceRequestStatus status = SicknessEvidenceRequestStatus.Pending)
    {
        var request = SicknessEvidenceRequest.Create(
            Guid.NewGuid(),
            companyId,
            recordId,
            Guid.Empty,
            dueDate,
            null,
            Now);

        if (status == SicknessEvidenceRequestStatus.Overdue)
        {
            request.MarkOverdue(Now);
        }
        else if (status == SicknessEvidenceRequestStatus.Fulfilled)
        {
            request.Fulfil(Now);
        }
        else if (status == SicknessEvidenceRequestStatus.Cancelled)
        {
            request.Cancel(Now);
        }

        db.SicknessEvidenceRequests.Add(request);
        await db.SaveChangesAsync();
        return request;
    }

    [Fact]
    public async Task ExecuteAsync_Sends_Reminder_For_Pending_Request_Due_Within_Two_Days()
    {
        await using var db = BuildContext();
        var (recordId, employeeId, companyId) = await SeedRecordAsync(db);
        var request = await SeedRequestAsync(db, recordId, companyId, Today.AddDays(2));

        var writer = new FakeNotificationWriter();
        var job = BuildJob(db, writer);

        await job.ExecuteAsync();

        var reminder = Assert.Single(writer.Written, n => n.Type == NotificationType.SicknessEvidenceReminder);
        Assert.Equal(companyId, reminder.CompanyId);
        Assert.Equal(employeeId, reminder.EmployeeId);
        Assert.Equal(request.Id, reminder.SourceEntityId);
        Assert.Equal(NotificationPriority.Normal, reminder.Priority);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Duplicate_Reminder_When_Already_Sent()
    {
        await using var db = BuildContext();
        var (recordId, _, companyId) = await SeedRecordAsync(db);
        await SeedRequestAsync(db, recordId, companyId, Today.AddDays(1));

        var writer = new FakeNotificationWriter();
        var job = BuildJob(db, writer);

        await job.ExecuteAsync();
        await job.ExecuteAsync();

        Assert.Single(writer.Written, n => n.Type == NotificationType.SicknessEvidenceReminder);
    }

    [Fact]
    public async Task ExecuteAsync_Marks_Overdue_And_Sends_Overdue_Notification_For_Pending_Request_Past_Due_Date()
    {
        await using var db = BuildContext();
        var (recordId, employeeId, companyId) = await SeedRecordAsync(db);
        var request = await SeedRequestAsync(db, recordId, companyId, Today.AddDays(-1));

        var writer = new FakeNotificationWriter();
        var events = new FakeIntegrationEventPublisher();
        var job = BuildJob(db, writer, events);

        await job.ExecuteAsync();

        var updated = await db.SicknessEvidenceRequests.SingleAsync(r => r.Id == request.Id);
        Assert.Equal(SicknessEvidenceRequestStatus.Overdue, updated.Status);

        var overdue = Assert.Single(writer.Written, n => n.Type == NotificationType.SicknessEvidenceOverdue);
        Assert.Equal(companyId, overdue.CompanyId);
        Assert.Equal(employeeId, overdue.EmployeeId);
        Assert.Equal(request.Id, overdue.SourceEntityId);
        Assert.Equal(NotificationPriority.High, overdue.Priority);

        var overdueEvent = Assert.Single(events.PublishedEvents.OfType<SicknessEvidenceOverdueIntegrationEvent>());
        Assert.Equal(companyId, overdueEvent.CompanyId);
        Assert.Equal(employeeId, overdueEvent.EmployeeId);
        Assert.Equal(recordId, overdueEvent.SicknessRecordId);
        Assert.Equal(request.Id, overdueEvent.EvidenceRequestId);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Touch_Request_Already_Marked_Overdue()
    {
        await using var db = BuildContext();
        var (recordId, _, companyId) = await SeedRecordAsync(db);
        await SeedRequestAsync(db, recordId, companyId, Today.AddDays(-5), SicknessEvidenceRequestStatus.Overdue);

        var writer = new FakeNotificationWriter();
        var events = new FakeIntegrationEventPublisher();
        var job = BuildJob(db, writer, events);

        await job.ExecuteAsync();
        await job.ExecuteAsync();

        Assert.DoesNotContain(writer.Written, n => n.Type == NotificationType.SicknessEvidenceOverdue);
        Assert.Empty(events.PublishedEvents.OfType<SicknessEvidenceOverdueIntegrationEvent>());
    }

    [Fact]
    public async Task ExecuteAsync_Ignores_Fulfilled_Requests_Regardless_Of_Due_Date()
    {
        await using var db = BuildContext();
        var (recordId, _, companyId) = await SeedRecordAsync(db);
        // Due date within reminder window AND a separate one already past due —
        // neither should generate any notification once fulfilled.
        await SeedRequestAsync(db, recordId, companyId, Today.AddDays(1), SicknessEvidenceRequestStatus.Fulfilled);
        await SeedRequestAsync(db, recordId, companyId, Today.AddDays(-3), SicknessEvidenceRequestStatus.Fulfilled);

        var writer = new FakeNotificationWriter();
        var job = BuildJob(db, writer);

        await job.ExecuteAsync();

        Assert.Empty(writer.Written);

        var requests = await db.SicknessEvidenceRequests.ToListAsync();
        Assert.All(requests, r => Assert.Equal(SicknessEvidenceRequestStatus.Fulfilled, r.Status));
    }

    [Fact]
    public async Task ExecuteAsync_Ignores_Cancelled_Requests_Regardless_Of_Due_Date()
    {
        await using var db = BuildContext();
        var (recordId, _, companyId) = await SeedRecordAsync(db);
        // Due date within reminder window AND a separate one already past due —
        // neither should generate any notification once cancelled.
        await SeedRequestAsync(db, recordId, companyId, Today.AddDays(1), SicknessEvidenceRequestStatus.Cancelled);
        await SeedRequestAsync(db, recordId, companyId, Today.AddDays(-3), SicknessEvidenceRequestStatus.Cancelled);

        var writer = new FakeNotificationWriter();
        var job = BuildJob(db, writer);

        await job.ExecuteAsync();

        Assert.Empty(writer.Written);

        var requests = await db.SicknessEvidenceRequests.ToListAsync();
        Assert.All(requests, r => Assert.Equal(SicknessEvidenceRequestStatus.Cancelled, r.Status));
    }
}
