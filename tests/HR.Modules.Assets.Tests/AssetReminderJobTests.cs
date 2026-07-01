using HR.Modules.Assets.Domain;
using HR.Modules.Assets.Features.CreateAsset;
using HR.Modules.Assets.Features.CreateAssetAssignment;
using HR.Modules.Assets.Features.CreateAssetCategory;
using HR.Modules.Assets.Jobs;
using HR.Modules.Assets.Persistence;
using HR.Modules.Assets.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Tests;

public class AssetReminderJobTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);

    private static AssetsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<AssetsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AssetsDbContext(options);
    }

    private static async Task<(Guid assignmentId, Guid employeeId, Guid companyId)> SeedActiveAssignmentAsync(
        AssetsDbContext db)
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var clock      = new FakeClock(FixedUtcNow);

        var categoryResult = await new CreateAssetCategoryHandler(db, clock).HandleAsync(
            new CreateAssetCategoryRequest { CompanyId = companyId, Name = "IT" },
            CancellationToken.None);

        var assetResult = await new CreateAssetHandler(db, clock, new FakeAuditPublisher()).HandleAsync(
            new CreateAssetRequest
            {
                CompanyId   = companyId,
                AssetNumber = "ASSET-001",
                CategoryId  = categoryResult.Value!.Id,
                Name        = "Laptop"
            }, CancellationToken.None);

        var assignmentResult = await new CreateAssetAssignmentHandler(
            db, clock, new FakeTaskCreator(), new FakeNotificationWriter(), new FakeAuditPublisher())
            .HandleAsync(new CreateAssetAssignmentRequest
            {
                CompanyId  = companyId,
                AssetId    = assetResult.Value!.Id,
                EmployeeId = employeeId,
                AssignedBy = Guid.NewGuid()
            }, CancellationToken.None);

        return (assignmentResult.Value!.Id, employeeId, companyId);
    }

    private static AssetReminderJob BuildJob(AssetsDbContext db, FakeNotificationWriter writer, FakeClock clock)
        => new(db, writer, clock);

    // ── Acknowledgement reminders ─────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Sends_Acknowledgement_Reminder_For_Unacknowledged_Active_Assignment()
    {
        await using var db = BuildContext();
        var (assignmentId, employeeId, companyId) = await SeedActiveAssignmentAsync(db);

        var writer = new FakeNotificationWriter();
        var job    = BuildJob(db, writer, new FakeClock(FixedUtcNow));

        await job.ExecuteAsync();

        var reminder = Assert.Single(writer.Written,
            n => n.Type == NotificationType.AssetAcknowledgementReminder);
        Assert.Equal(companyId,    reminder.CompanyId);
        Assert.Equal(employeeId,   reminder.EmployeeId);
        Assert.Equal(assignmentId, reminder.SourceEntityId);
        Assert.Equal(NotificationPriority.Normal, reminder.Priority);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Duplicate_Acknowledgement_Reminder_When_Already_Sent()
    {
        await using var db = BuildContext();
        await SeedActiveAssignmentAsync(db);

        var writer = new FakeNotificationWriter();
        var job    = BuildJob(db, writer, new FakeClock(FixedUtcNow));

        // Run twice — second run sees the existing reminder via ExistsAsync and skips
        await job.ExecuteAsync();
        await job.ExecuteAsync();

        Assert.Single(writer.Written, n => n.Type == NotificationType.AssetAcknowledgementReminder);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Send_Acknowledgement_Reminder_For_Acknowledged_Assignment()
    {
        await using var db = BuildContext();
        var (assignmentId, _, _) = await SeedActiveAssignmentAsync(db);

        var assignment = await db.AssetAssignments.FindAsync(assignmentId);
        assignment!.Acknowledge(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero));
        await db.SaveChangesAsync();

        var writer = new FakeNotificationWriter();
        var job    = BuildJob(db, writer, new FakeClock(FixedUtcNow));

        await job.ExecuteAsync();

        Assert.DoesNotContain(writer.Written, n => n.Type == NotificationType.AssetAcknowledgementReminder);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Send_Acknowledgement_Reminder_For_Returned_Assignment()
    {
        await using var db = BuildContext();
        var (assignmentId, _, _) = await SeedActiveAssignmentAsync(db);

        var assignment = await db.AssetAssignments.FindAsync(assignmentId);
        assignment!.Return(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero));
        await db.SaveChangesAsync();

        var writer = new FakeNotificationWriter();
        var job    = BuildJob(db, writer, new FakeClock(FixedUtcNow));

        await job.ExecuteAsync();

        Assert.DoesNotContain(writer.Written, n => n.Type == NotificationType.AssetAcknowledgementReminder);
    }

    // ── Return reminders ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Sends_Return_Reminder_When_Return_Was_Requested_But_Not_Completed()
    {
        await using var db = BuildContext();
        var (assignmentId, employeeId, companyId) = await SeedActiveAssignmentAsync(db);

        var writer = new FakeNotificationWriter();
        var now    = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        // Seed AssetReturnRequested to simulate a prior RequestAssetReturn call
        await writer.WriteAsync(
            Guid.NewGuid(), companyId, employeeId,
            "Asset return requested", null,
            assignmentId,
            NotificationType.AssetReturnRequested,
            NotificationPriority.Normal,
            now);

        var job = BuildJob(db, writer, new FakeClock(FixedUtcNow));

        await job.ExecuteAsync();

        var reminder = Assert.Single(writer.Written,
            n => n.Type == NotificationType.AssetReturnReminder);
        Assert.Equal(companyId,    reminder.CompanyId);
        Assert.Equal(employeeId,   reminder.EmployeeId);
        Assert.Equal(assignmentId, reminder.SourceEntityId);
        Assert.Equal(NotificationPriority.High, reminder.Priority);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Duplicate_Return_Reminder_When_Already_Sent()
    {
        await using var db = BuildContext();
        var (assignmentId, employeeId, companyId) = await SeedActiveAssignmentAsync(db);

        var writer = new FakeNotificationWriter();
        var now    = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        await writer.WriteAsync(
            Guid.NewGuid(), companyId, employeeId,
            "Asset return requested", null,
            assignmentId,
            NotificationType.AssetReturnRequested,
            NotificationPriority.Normal,
            now);

        var job = BuildJob(db, writer, new FakeClock(FixedUtcNow));

        await job.ExecuteAsync();
        await job.ExecuteAsync();

        Assert.Single(writer.Written, n => n.Type == NotificationType.AssetReturnReminder);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Send_Return_Reminder_When_No_Return_Was_Requested()
    {
        await using var db = BuildContext();
        await SeedActiveAssignmentAsync(db);

        var writer = new FakeNotificationWriter();
        var job    = BuildJob(db, writer, new FakeClock(FixedUtcNow));

        await job.ExecuteAsync();

        Assert.DoesNotContain(writer.Written, n => n.Type == NotificationType.AssetReturnReminder);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Send_Return_Reminder_When_Asset_Already_Returned()
    {
        await using var db = BuildContext();
        var (assignmentId, employeeId, companyId) = await SeedActiveAssignmentAsync(db);

        var now        = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var assignment = await db.AssetAssignments.FindAsync(assignmentId);
        assignment!.Return(now);
        await db.SaveChangesAsync();

        // Even if a return-requested notification exists, no reminder should go out
        var writer = new FakeNotificationWriter();
        await writer.WriteAsync(
            Guid.NewGuid(), companyId, employeeId,
            "Asset return requested", null,
            assignmentId,
            NotificationType.AssetReturnRequested,
            NotificationPriority.Normal,
            now);

        var job = BuildJob(db, writer, new FakeClock(FixedUtcNow));

        await job.ExecuteAsync();

        Assert.DoesNotContain(writer.Written, n => n.Type == NotificationType.AssetReturnReminder);
    }

    // ── Acknowledgement overdue ───────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Sends_Acknowledgement_Overdue_When_Unacknowledged_For_More_Than_7_Days()
    {
        await using var db = BuildContext();
        var (assignmentId, employeeId, companyId) = await SeedActiveAssignmentAsync(db);

        // Backdate AssignedAt to 8 days ago
        var assignment = await db.AssetAssignments.FindAsync(assignmentId);
        var pastDate = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero).AddDays(-8);
        typeof(AssetAssignment)
            .GetProperty("AssignedAt")!
            .SetValue(assignment, pastDate);
        await db.SaveChangesAsync();

        var writer = new FakeNotificationWriter();
        var job    = BuildJob(db, writer, new FakeClock(FixedUtcNow));

        await job.ExecuteAsync();

        var overdue = Assert.Single(writer.Written,
            n => n.Type == NotificationType.AssetAcknowledgementOverdue);
        Assert.Equal(companyId,    overdue.CompanyId);
        Assert.Equal(employeeId,   overdue.EmployeeId);
        Assert.Equal(assignmentId, overdue.SourceEntityId);
        Assert.Equal(NotificationPriority.High, overdue.Priority);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Send_Acknowledgement_Overdue_When_Within_7_Days()
    {
        await using var db = BuildContext();
        await SeedActiveAssignmentAsync(db);
        // Assignment was seeded at FixedUtcNow (0 days ago) — not yet overdue

        var writer = new FakeNotificationWriter();
        var job    = BuildJob(db, writer, new FakeClock(FixedUtcNow));

        await job.ExecuteAsync();

        Assert.DoesNotContain(writer.Written, n => n.Type == NotificationType.AssetAcknowledgementOverdue);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Duplicate_Acknowledgement_Overdue_When_Already_Sent()
    {
        await using var db = BuildContext();
        var (assignmentId, employeeId, companyId) = await SeedActiveAssignmentAsync(db);

        var assignment = await db.AssetAssignments.FindAsync(assignmentId);
        var pastDate = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero).AddDays(-8);
        typeof(AssetAssignment)
            .GetProperty("AssignedAt")!
            .SetValue(assignment, pastDate);
        await db.SaveChangesAsync();

        var writer = new FakeNotificationWriter();
        var job    = BuildJob(db, writer, new FakeClock(FixedUtcNow));

        await job.ExecuteAsync();
        await job.ExecuteAsync();

        Assert.Single(writer.Written, n => n.Type == NotificationType.AssetAcknowledgementOverdue);
    }

    // ── Return overdue ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Sends_Return_Overdue_When_Reminder_Already_Sent()
    {
        await using var db = BuildContext();
        var (assignmentId, employeeId, companyId) = await SeedActiveAssignmentAsync(db);

        var now    = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var writer = new FakeNotificationWriter();

        // Seed prior notifications: return requested and reminder already sent
        await writer.WriteAsync(
            Guid.NewGuid(), companyId, employeeId,
            "Asset return requested", null,
            assignmentId, NotificationType.AssetReturnRequested,
            NotificationPriority.Normal, now);

        await writer.WriteAsync(
            Guid.NewGuid(), companyId, employeeId,
            "Reminder: please return your assigned asset", null,
            assignmentId, NotificationType.AssetReturnReminder,
            NotificationPriority.High, now);

        var job = BuildJob(db, writer, new FakeClock(FixedUtcNow));

        await job.ExecuteAsync();

        var overdue = Assert.Single(writer.Written,
            n => n.Type == NotificationType.AssetReturnOverdue);
        Assert.Equal(companyId,    overdue.CompanyId);
        Assert.Equal(employeeId,   overdue.EmployeeId);
        Assert.Equal(assignmentId, overdue.SourceEntityId);
        Assert.Equal(NotificationPriority.Urgent, overdue.Priority);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Send_Return_Overdue_When_No_Reminder_Sent_Yet()
    {
        await using var db = BuildContext();
        var (assignmentId, employeeId, companyId) = await SeedActiveAssignmentAsync(db);

        var now    = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var writer = new FakeNotificationWriter();

        // Only seed the return-requested notification, not the reminder
        await writer.WriteAsync(
            Guid.NewGuid(), companyId, employeeId,
            "Asset return requested", null,
            assignmentId, NotificationType.AssetReturnRequested,
            NotificationPriority.Normal, now);

        var job = BuildJob(db, writer, new FakeClock(FixedUtcNow));

        await job.ExecuteAsync();

        Assert.DoesNotContain(writer.Written, n => n.Type == NotificationType.AssetReturnOverdue);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Duplicate_Return_Overdue_When_Already_Sent()
    {
        await using var db = BuildContext();
        var (assignmentId, employeeId, companyId) = await SeedActiveAssignmentAsync(db);

        var now    = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var writer = new FakeNotificationWriter();

        await writer.WriteAsync(
            Guid.NewGuid(), companyId, employeeId,
            "Asset return requested", null,
            assignmentId, NotificationType.AssetReturnRequested,
            NotificationPriority.Normal, now);

        await writer.WriteAsync(
            Guid.NewGuid(), companyId, employeeId,
            "Reminder: please return your assigned asset", null,
            assignmentId, NotificationType.AssetReturnReminder,
            NotificationPriority.High, now);

        var job = BuildJob(db, writer, new FakeClock(FixedUtcNow));

        await job.ExecuteAsync();
        await job.ExecuteAsync();

        Assert.Single(writer.Written, n => n.Type == NotificationType.AssetReturnOverdue);
    }
}
