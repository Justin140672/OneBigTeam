using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Jobs;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests.Jobs;

public class ReturnToWorkReminderJobTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 15, 2, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedUtcNow);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static SicknessDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<SicknessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ReturnToWorkReminderJob BuildJob(
        SicknessDbContext db,
        FakeNotificationWriter writer,
        Guid? managerId) =>
        new(db, writer, new FakeManagerReader(managerId), new FakeClock(FixedUtcNow),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ReturnToWorkReminderJob>.Instance);

    private static async Task<ReturnToWorkReview> SeedReviewAsync(
        SicknessDbContext db,
        Guid companyId,
        Guid employeeId,
        DateOnly dueDate,
        ReturnToWorkReviewStatus status = ReturnToWorkReviewStatus.Pending)
    {
        var categoryId = Guid.NewGuid();
        db.SicknessCategories.Add(SicknessCategory.Create(categoryId, companyId, "Cold", 1, Now));

        var record = SicknessRecord.Create(
            Guid.NewGuid(), companyId, employeeId, categoryId,
            new DateOnly(2026, 6, 1), SicknessDayPart.FullDay,
            new DateOnly(2026, 6, 5), SicknessDayPart.FullDay,
            totalDays: 5m, notes: null,
            evidenceStatus: SicknessEvidenceStatus.NotRequired, now: Now);
        db.SicknessRecords.Add(record);

        var review = ReturnToWorkReview.Create(Guid.NewGuid(), companyId, record.Id, employeeId, dueDate, Now);

        if (status == ReturnToWorkReviewStatus.Overdue)
        {
            review.MarkOverdue(Now);
        }
        else if (status == ReturnToWorkReviewStatus.Completed)
        {
            review.Complete(Guid.NewGuid(), FitToReturnOutcome.Fit, false, null, null, Now);
        }
        else if (status == ReturnToWorkReviewStatus.Cancelled)
        {
            review.Cancel(Now);
        }

        db.ReturnToWorkReviews.Add(review);
        await db.SaveChangesAsync();
        return review;
    }

    [Fact]
    public async Task ExecuteAsync_Sends_Reminder_To_Manager_For_Pending_Review_Due_Within_Two_Days()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId  = Guid.NewGuid();
        var review = await SeedReviewAsync(db, companyId, employeeId, Today.AddDays(2));

        var writer = new FakeNotificationWriter();
        var job = BuildJob(db, writer, managerId);

        await job.ExecuteAsync();

        var reminder = Assert.Single(writer.Written, n => n.Type == NotificationType.ReturnToWorkReviewReminder);
        Assert.Equal(companyId, reminder.CompanyId);
        Assert.Equal(managerId, reminder.EmployeeId);
        Assert.Equal(review.Id, reminder.SourceEntityId);
        Assert.Equal(NotificationPriority.Normal, reminder.Priority);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Duplicate_Reminder_When_Already_Sent()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId  = Guid.NewGuid();
        await SeedReviewAsync(db, companyId, employeeId, Today.AddDays(1));

        var writer = new FakeNotificationWriter();
        var job = BuildJob(db, writer, managerId);

        await job.ExecuteAsync();
        await job.ExecuteAsync();

        Assert.Single(writer.Written, n => n.Type == NotificationType.ReturnToWorkReviewReminder);
    }

    [Fact]
    public async Task ExecuteAsync_Skips_Reminder_When_Employee_Has_No_Manager()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        await SeedReviewAsync(db, companyId, employeeId, Today.AddDays(1));

        var writer = new FakeNotificationWriter();
        var job = BuildJob(db, writer, managerId: null);

        await job.ExecuteAsync();

        Assert.Empty(writer.Written);
    }

    [Fact]
    public async Task ExecuteAsync_Marks_Overdue_And_Notifies_Manager_For_Pending_Review_Past_Due_Date()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId  = Guid.NewGuid();
        var review = await SeedReviewAsync(db, companyId, employeeId, Today.AddDays(-1));

        var writer = new FakeNotificationWriter();
        var job = BuildJob(db, writer, managerId);

        await job.ExecuteAsync();

        var updated = await db.ReturnToWorkReviews.SingleAsync(r => r.Id == review.Id);
        Assert.Equal(ReturnToWorkReviewStatus.Overdue, updated.Status);

        var overdue = Assert.Single(writer.Written, n => n.Type == NotificationType.ReturnToWorkReviewOverdue);
        Assert.Equal(companyId, overdue.CompanyId);
        Assert.Equal(managerId, overdue.EmployeeId);
        Assert.Equal(review.Id, overdue.SourceEntityId);
        Assert.Equal(NotificationPriority.High, overdue.Priority);
    }

    [Fact]
    public async Task ExecuteAsync_Marks_Overdue_Even_When_Employee_Has_No_Manager()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var review = await SeedReviewAsync(db, companyId, employeeId, Today.AddDays(-1));

        var writer = new FakeNotificationWriter();
        var job = BuildJob(db, writer, managerId: null);

        await job.ExecuteAsync();

        var updated = await db.ReturnToWorkReviews.SingleAsync(r => r.Id == review.Id);
        Assert.Equal(ReturnToWorkReviewStatus.Overdue, updated.Status);
        Assert.Empty(writer.Written);
    }

    [Fact]
    public async Task ExecuteAsync_Reconciles_Missing_Overdue_Notification_For_Already_Overdue_Review_Exactly_Once()
    {
        // OBT-REM-04: a review already persisted as Overdue whose overdue notification was never
        // sent (prior run crashed after the status commit) gets that notification reconciled on a
        // later run — exactly once across repeated runs.
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId  = Guid.NewGuid();
        await SeedReviewAsync(db, companyId, employeeId, Today.AddDays(-5), ReturnToWorkReviewStatus.Overdue);

        var writer = new FakeNotificationWriter();
        var job = BuildJob(db, writer, managerId);

        await job.ExecuteAsync();
        await job.ExecuteAsync();

        Assert.Single(writer.Written, n => n.Type == NotificationType.ReturnToWorkReviewOverdue);
    }

    [Fact]
    public async Task ExecuteAsync_Ignores_Completed_Reviews_Regardless_Of_Due_Date()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId  = Guid.NewGuid();
        await SeedReviewAsync(db, companyId, employeeId, Today.AddDays(1), ReturnToWorkReviewStatus.Completed);
        await SeedReviewAsync(db, companyId, employeeId, Today.AddDays(-3), ReturnToWorkReviewStatus.Completed);

        var writer = new FakeNotificationWriter();
        var job = BuildJob(db, writer, managerId);

        await job.ExecuteAsync();

        Assert.Empty(writer.Written);

        var reviews = await db.ReturnToWorkReviews.ToListAsync();
        Assert.All(reviews, r => Assert.Equal(ReturnToWorkReviewStatus.Completed, r.Status));
    }

    [Fact]
    public async Task ExecuteAsync_Ignores_Cancelled_Reviews_Regardless_Of_Due_Date()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId  = Guid.NewGuid();
        await SeedReviewAsync(db, companyId, employeeId, Today.AddDays(1), ReturnToWorkReviewStatus.Cancelled);
        await SeedReviewAsync(db, companyId, employeeId, Today.AddDays(-3), ReturnToWorkReviewStatus.Cancelled);

        var writer = new FakeNotificationWriter();
        var job = BuildJob(db, writer, managerId);

        await job.ExecuteAsync();

        Assert.Empty(writer.Written);

        var reviews = await db.ReturnToWorkReviews.ToListAsync();
        Assert.All(reviews, r => Assert.Equal(ReturnToWorkReviewStatus.Cancelled, r.Status));
    }
}
