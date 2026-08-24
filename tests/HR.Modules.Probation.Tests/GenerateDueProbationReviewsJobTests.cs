using HR.Modules.Tasks.Contracts;
using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Jobs;
using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Probation.Tests;

public class GenerateDueProbationReviewsJobTests
{
    // 3-month probation: 2026-01-01 → 2026-04-01 (90 days)
    // ManagerCheckIn at day 30 = 2026-01-31
    // HrReview       at day 60 = 2026-03-02
    // FinalDecision  at        = 2026-04-01
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly ExpectedEndDate = new(2026, 4, 1);
    private static readonly DateTimeOffset SeedNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_Creates_ManagerCheckIn_When_Due()
    {
        await using var context = BuildContext();
        await SeedActiveRecord(context);

        await BuildJob(context, today: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)).ExecuteAsync();

        var reviews = await context.ProbationReviews.ToListAsync();
        Assert.Single(reviews);
        Assert.Equal(ProbationReviewType.ManagerCheckIn, reviews[0].ReviewType);
        Assert.Equal(ProbationReviewStatus.Pending, reviews[0].Status);
    }

    [Fact]
    public async Task ExecuteAsync_Creates_All_Reviews_Due_In_One_Pass()
    {
        await using var context = BuildContext();
        await SeedActiveRecord(context);

        // Mar 15: ManagerCheckIn (Jan 31) and HrReview (Mar 2) both due
        await BuildJob(context, today: new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc)).ExecuteAsync();

        var reviews = await context.ProbationReviews.OrderBy(r => r.DueDate).ToListAsync();
        Assert.Equal(2, reviews.Count);
        Assert.Equal(ProbationReviewType.ManagerCheckIn, reviews[0].ReviewType);
        Assert.Equal(ProbationReviewType.HrReview, reviews[1].ReviewType);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Create_Reviews_Not_Yet_Due()
    {
        await using var context = BuildContext();
        await SeedActiveRecord(context);

        // Jan 15: ManagerCheckIn (Jan 31) not yet due
        await BuildJob(context, today: new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)).ExecuteAsync();

        Assert.Empty(await context.ProbationReviews.ToListAsync());
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Create_Duplicate_Pending_Review()
    {
        await using var context = BuildContext();
        var record = await SeedActiveRecord(context);

        context.ProbationReviews.Add(ProbationReview.Create(
            Guid.NewGuid(), record.CompanyId, record.Id,
            ProbationReviewType.ManagerCheckIn, new DateOnly(2026, 1, 31), SeedNow));
        await context.SaveChangesAsync();

        await BuildJob(context, today: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)).ExecuteAsync();

        Assert.Single(await context.ProbationReviews
            .Where(r => r.ReviewType == ProbationReviewType.ManagerCheckIn)
            .ToListAsync());
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Create_Duplicate_Of_Completed_Review()
    {
        await using var context = BuildContext();
        var record = await SeedActiveRecord(context);

        var existing = ProbationReview.Create(
            Guid.NewGuid(), record.CompanyId, record.Id,
            ProbationReviewType.ManagerCheckIn, new DateOnly(2026, 1, 31), SeedNow);
        existing.Complete(Guid.NewGuid(), null, null, SeedNow);
        context.ProbationReviews.Add(existing);
        await context.SaveChangesAsync();

        await BuildJob(context, today: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)).ExecuteAsync();

        Assert.Single(await context.ProbationReviews.ToListAsync());
    }

    [Fact]
    public async Task ExecuteAsync_Transitions_Active_Record_To_ReviewDue()
    {
        await using var context = BuildContext();
        await SeedActiveRecord(context);

        await BuildJob(context, today: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)).ExecuteAsync();

        var record = await context.ProbationRecords.SingleAsync();
        Assert.Equal(ProbationStatus.ReviewDue, record.Status);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Regress_ReviewDue_Record_Status()
    {
        await using var context = BuildContext();
        var record = await SeedActiveRecord(context);
        record.MarkReviewDue(SeedNow);
        await context.SaveChangesAsync();

        await BuildJob(context, today: new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc)).ExecuteAsync();

        var updated = await context.ProbationRecords.SingleAsync();
        Assert.Equal(ProbationStatus.ReviewDue, updated.Status);
    }

    [Fact]
    public async Task ExecuteAsync_Skips_Passed_Records()
    {
        await using var context = BuildContext();
        var record = await SeedActiveRecord(context);
        record.Pass(Guid.NewGuid(), ExpectedEndDate, null, SeedNow);
        await context.SaveChangesAsync();

        await BuildJob(context, today: new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)).ExecuteAsync();

        Assert.Empty(await context.ProbationReviews.ToListAsync());
    }

    [Fact]
    public async Task ExecuteAsync_Skips_Failed_Records()
    {
        await using var context = BuildContext();
        var record = await SeedActiveRecord(context);
        record.Fail(Guid.NewGuid(), ExpectedEndDate, null, SeedNow);
        await context.SaveChangesAsync();

        await BuildJob(context, today: new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)).ExecuteAsync();

        Assert.Empty(await context.ProbationReviews.ToListAsync());
    }

    [Fact]
    public async Task ExecuteAsync_Does_Nothing_When_No_Active_Records_Exist()
    {
        await using var context = BuildContext();

        await BuildJob(context, today: new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)).ExecuteAsync();

        Assert.Empty(await context.ProbationReviews.ToListAsync());
    }

    [Fact]
    public async Task ExecuteAsync_Creates_Reviews_For_Multiple_Records()
    {
        await using var context = BuildContext();
        await SeedActiveRecord(context);
        await SeedActiveRecord(context);

        await BuildJob(context, today: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)).ExecuteAsync();

        var reviews = await context.ProbationReviews.ToListAsync();
        Assert.Equal(2, reviews.Count);
        Assert.All(reviews, r => Assert.Equal(ProbationReviewType.ManagerCheckIn, r.ReviewType));
    }

    [Fact]
    public async Task ExecuteAsync_Creates_Task_For_Each_Generated_Review()
    {
        await using var context = BuildContext();
        await SeedActiveRecord(context);
        await SeedActiveRecord(context);

        var taskCreator = new FakeTaskCreator();
        await BuildJob(context, today: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), taskCreator: taskCreator).ExecuteAsync();

        Assert.Equal(2, taskCreator.Created.Count);
    }

    [Fact]
    public async Task ExecuteAsync_Task_Is_Assigned_To_Manager()
    {
        await using var context = BuildContext();
        var managerId = Guid.NewGuid();
        await SeedActiveRecord(context, managerEmployeeId: managerId);

        var taskCreator = new FakeTaskCreator();
        await BuildJob(context, today: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), taskCreator: taskCreator).ExecuteAsync();

        Assert.Equal(managerId, taskCreator.Created[0].AssignedEmployeeId);
        Assert.Equal(managerId, taskCreator.Created[0].AssignedUserId);
    }

    [Fact]
    public async Task ExecuteAsync_Task_Title_Includes_Employee_Name()
    {
        await using var context = BuildContext();
        var employeeId = Guid.NewGuid();
        await SeedActiveRecord(context, employeeId: employeeId);

        var taskCreator = new FakeTaskCreator();
        var nameReader = new FakeEmployeeNameReader(new Dictionary<Guid, string> { [employeeId] = "Jane Doe" });
        await BuildJob(context, today: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), taskCreator: taskCreator, employeeNameReader: nameReader).ExecuteAsync();

        Assert.Equal("Complete probation review — Jane Doe", taskCreator.Created[0].Title);
    }

    [Fact]
    public async Task ExecuteAsync_Task_Title_Falls_Back_When_Name_Unknown()
    {
        await using var context = BuildContext();
        await SeedActiveRecord(context);

        var taskCreator = new FakeTaskCreator();
        await BuildJob(context, today: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), taskCreator: taskCreator).ExecuteAsync();

        Assert.Equal("Complete probation review — Unknown Employee", taskCreator.Created[0].Title);
    }

    [Fact]
    public async Task ExecuteAsync_Task_Source_Is_Probation()
    {
        await using var context = BuildContext();
        await SeedActiveRecord(context);

        var taskCreator = new FakeTaskCreator();
        await BuildJob(context, today: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), taskCreator: taskCreator).ExecuteAsync();

        Assert.Equal(TaskSource.Probation, taskCreator.Created[0].Source);
        Assert.Equal(TaskActionType.Review, taskCreator.Created[0].ActionType);
    }

    [Fact]
    public async Task ExecuteAsync_Task_Priority_Is_High()
    {
        await using var context = BuildContext();
        await SeedActiveRecord(context);

        var taskCreator = new FakeTaskCreator();
        await BuildJob(context, today: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), taskCreator: taskCreator).ExecuteAsync();

        Assert.Equal(TaskPriority.High, taskCreator.Created[0].Priority);
    }

    [Fact]
    public async Task ExecuteAsync_Task_DueDate_Matches_Review_DueDate()
    {
        await using var context = BuildContext();
        await SeedActiveRecord(context);

        var taskCreator = new FakeTaskCreator();
        await BuildJob(context, today: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), taskCreator: taskCreator).ExecuteAsync();

        Assert.Equal(new DateOnly(2026, 1, 31), taskCreator.Created[0].DueDate);
    }

    [Fact]
    public async Task ExecuteAsync_Task_SourceEntityId_Is_ReviewId()
    {
        await using var context = BuildContext();
        await SeedActiveRecord(context);

        var taskCreator = new FakeTaskCreator();
        await BuildJob(context, today: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), taskCreator: taskCreator).ExecuteAsync();

        var review = await context.ProbationReviews.SingleAsync();
        Assert.Equal(review.Id, taskCreator.Created[0].SourceEntityId);
    }

    [Fact]
    public async Task ExecuteAsync_No_Tasks_Created_When_No_Reviews_Due()
    {
        await using var context = BuildContext();
        await SeedActiveRecord(context);

        var taskCreator = new FakeTaskCreator();
        await BuildJob(context, today: new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), taskCreator: taskCreator).ExecuteAsync();

        Assert.Empty(taskCreator.Created);
    }

    [Fact]
    public async Task ExecuteAsync_Uses_Company_Local_Day_Not_UTC_Day_When_Determining_Review_Is_Due()
    {
        // ManagerCheckIn is due 2026-01-31. At 2026-01-30T23:30:00Z it's still Jan 30 in UTC, but
        // already Jan 31 11:30 in "Etc/GMT-12" (a fixed UTC+12 zone, no DST) — the review must be
        // created based on the company's local day, not the UTC day.
        await using var context = BuildContext();
        await SeedActiveRecord(context);

        var utcNow = new DateTime(2026, 1, 30, 23, 30, 0, DateTimeKind.Utc);
        await BuildJob(
            context,
            today: utcNow,
            companyTimeZoneReader: new FakeCompanyTimeZoneReader("Etc/GMT-12")).ExecuteAsync();

        var reviews = await context.ProbationReviews.ToListAsync();
        Assert.Single(reviews);
        Assert.Equal(ProbationReviewType.ManagerCheckIn, reviews[0].ReviewType);
    }

    [Fact]
    public async Task ExecuteAsync_Uses_Company_Custom_Checkpoint_Days_When_Configured()
    {
        // Custom [14, 45] checkpoints instead of default [30, 60, 90]: ManagerCheckIn due 2026-01-15.
        await using var context = BuildContext();
        await SeedActiveRecord(context);

        await BuildJob(
            context,
            today: new DateTime(2026, 1, 16, 0, 0, 0, DateTimeKind.Utc),
            companyProbationSettingsReader: new FakeCompanyProbationSettingsReader([14, 45])).ExecuteAsync();

        var reviews = await context.ProbationReviews.ToListAsync();
        Assert.Single(reviews);
        Assert.Equal(ProbationReviewType.ManagerCheckIn, reviews[0].ReviewType);
        Assert.Equal(new DateOnly(2026, 1, 15), reviews[0].DueDate);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Create_Review_For_Default_Checkpoint_Day_When_Company_Uses_Custom_Days()
    {
        // Default day-30 checkpoint (2026-01-31) must not fire for a company configured with [14, 45].
        await using var context = BuildContext();
        await SeedActiveRecord(context);

        await BuildJob(
            context,
            today: new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            companyProbationSettingsReader: new FakeCompanyProbationSettingsReader([14, 45])).ExecuteAsync();

        var reviews = await context.ProbationReviews.ToListAsync();
        Assert.Single(reviews); // only the day-14 ManagerCheckIn, already due by Jan 31
        Assert.Equal(new DateOnly(2026, 1, 15), reviews[0].DueDate);
    }

    [Fact]
    public async Task ExecuteAsync_HrReview_Task_Assigned_To_Hr_Admin_Not_Manager_When_Single_Admin()
    {
        await using var context = BuildContext();
        var managerId = Guid.NewGuid();
        var hrAdminId = Guid.NewGuid();
        var record = await SeedActiveRecord(context, managerEmployeeId: managerId);

        var hrAdministratorDirectory = new FakeHrAdministratorDirectory();
        hrAdministratorDirectory.Seed(record.CompanyId, hrAdminId);

        var taskCreator = new FakeTaskCreator();
        // Mar 15: ManagerCheckIn (Jan 31) and HrReview (Mar 2) both due.
        await BuildJob(
            context,
            today: new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            taskCreator: taskCreator,
            hrAdministratorDirectory: hrAdministratorDirectory).ExecuteAsync();

        var hrReview = await context.ProbationReviews.SingleAsync(r => r.ReviewType == ProbationReviewType.HrReview);
        var hrReviewTask = taskCreator.Created.Single(t => t.SourceEntityId == hrReview.Id);

        Assert.Equal(hrAdminId, hrReviewTask.AssignedEmployeeId);
        Assert.NotEqual(managerId, hrReviewTask.AssignedEmployeeId);
    }

    [Fact]
    public async Task ExecuteAsync_HrReview_Task_Assignee_Is_Deterministic_And_All_Hr_Admins_Notified_When_Multiple_Admins()
    {
        await using var context = BuildContext();
        var record = await SeedActiveRecord(context);

        var lowest = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var higher = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var middle = Guid.Parse("77777777-7777-7777-7777-777777777777");

        var hrAdministratorDirectory = new FakeHrAdministratorDirectory();
        hrAdministratorDirectory.Seed(record.CompanyId, higher, lowest, middle);

        var taskCreator = new FakeTaskCreator();
        var notificationWriter = new FakeNotificationWriter();
        await BuildJob(
            context,
            today: new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            taskCreator: taskCreator,
            hrAdministratorDirectory: hrAdministratorDirectory,
            notificationWriter: notificationWriter).ExecuteAsync();

        var hrReview = await context.ProbationReviews.SingleAsync(r => r.ReviewType == ProbationReviewType.HrReview);
        var hrReviewTask = taskCreator.Created.Single(t => t.SourceEntityId == hrReview.Id);
        Assert.Equal(lowest, hrReviewTask.AssignedEmployeeId);

        var hrReviewNotifications = notificationWriter.Written
            .Where(n => n.SourceEntityId == hrReview.Id && n.Type == NotificationType.ProbationReviewDue)
            .ToList();
        Assert.Equal(3, hrReviewNotifications.Count);
        Assert.Contains(hrReviewNotifications, n => n.EmployeeId == lowest);
        Assert.Contains(hrReviewNotifications, n => n.EmployeeId == higher);
        Assert.Contains(hrReviewNotifications, n => n.EmployeeId == middle);
    }

    [Fact]
    public async Task ExecuteAsync_ManagerCheckIn_Task_And_Notification_Go_To_Current_Manager()
    {
        await using var context = BuildContext();
        var managerId = Guid.NewGuid();
        var record = await SeedActiveRecord(context, managerEmployeeId: managerId);

        var taskCreator = new FakeTaskCreator();
        var notificationWriter = new FakeNotificationWriter();
        await BuildJob(
            context,
            today: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            taskCreator: taskCreator,
            notificationWriter: notificationWriter).ExecuteAsync();

        var review = await context.ProbationReviews.SingleAsync();
        var task = Assert.Single(taskCreator.Created);
        Assert.Equal(managerId, task.AssignedEmployeeId);

        var notification = Assert.Single(notificationWriter.Written.Where(n => n.SourceEntityId == review.Id));
        Assert.Equal(managerId, notification.EmployeeId);
    }

    [Fact]
    public async Task ExecuteAsync_HrReview_With_No_Hr_Admins_Creates_Task_With_Null_Assignee_And_No_Notification_But_Does_Not_Crash()
    {
        await using var context = BuildContext();
        var record = await SeedActiveRecord(context);

        var taskCreator = new FakeTaskCreator();
        var notificationWriter = new FakeNotificationWriter();
        // Mar 15: ManagerCheckIn and HrReview both due; no HR admins configured (default empty).
        var exception = await Record.ExceptionAsync(() => BuildJob(
            context,
            today: new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            taskCreator: taskCreator,
            notificationWriter: notificationWriter).ExecuteAsync());

        Assert.Null(exception);

        var hrReview = await context.ProbationReviews.SingleAsync(r => r.ReviewType == ProbationReviewType.HrReview);
        var hrReviewTask = taskCreator.Created.Single(t => t.SourceEntityId == hrReview.Id);
        Assert.Null(hrReviewTask.AssignedEmployeeId);
        Assert.Empty(notificationWriter.Written.Where(n => n.SourceEntityId == hrReview.Id));

        // The ManagerCheckIn review for the same record is still created and notified normally.
        var managerCheckInReview = await context.ProbationReviews
            .SingleAsync(r => r.ReviewType == ProbationReviewType.ManagerCheckIn);
        Assert.Contains(taskCreator.Created, t => t.SourceEntityId == managerCheckInReview.Id);
    }

    [Fact]
    public async Task ExecuteAsync_Running_Job_Twice_Does_Not_Create_Duplicate_ReviewDue_Notifications()
    {
        await using var context = BuildContext();
        var managerId = Guid.NewGuid();
        await SeedActiveRecord(context, managerEmployeeId: managerId);

        var notificationWriter = new FakeNotificationWriter();

        await BuildJob(
            context,
            today: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            notificationWriter: notificationWriter).ExecuteAsync();

        var notificationCountAfterFirst = notificationWriter.Written.Count;
        Assert.Equal(1, notificationCountAfterFirst);

        // Second run over the same day: no new reviews are due (guarded by the "duplicate pending
        // review" check), so re-running should not add any further notifications either.
        await BuildJob(
            context,
            today: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            notificationWriter: notificationWriter).ExecuteAsync();

        Assert.Equal(notificationCountAfterFirst, notificationWriter.Written.Count);
    }

    private async Task<ProbationRecord> SeedActiveRecord(
        ProbationDbContext context,
        Guid? employeeId = null,
        Guid? managerEmployeeId = null)
    {
        var record = ProbationRecord.Create(
            Guid.NewGuid(), Guid.NewGuid(),
            employeeId ?? Guid.NewGuid(),
            managerEmployeeId ?? Guid.NewGuid(),
            StartDate, ExpectedEndDate, null, SeedNow);
        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();
        return record;
    }

    private static GenerateDueProbationReviewsJob BuildJob(
        ProbationDbContext context,
        DateTime today,
        FakeTaskCreator? taskCreator = null,
        FakeEmployeeNameReader? employeeNameReader = null,
        FakeCompanyTimeZoneReader? companyTimeZoneReader = null,
        FakeCompanyProbationSettingsReader? companyProbationSettingsReader = null,
        FakeHrAdministratorDirectory? hrAdministratorDirectory = null,
        FakeNotificationWriter? notificationWriter = null) =>
        new(context,
            new FakeClock(today),
            companyTimeZoneReader ?? new FakeCompanyTimeZoneReader(),
            companyProbationSettingsReader ?? new FakeCompanyProbationSettingsReader(),
            taskCreator ?? new FakeTaskCreator(),
            employeeNameReader ?? new FakeEmployeeNameReader(),
            hrAdministratorDirectory ?? new FakeHrAdministratorDirectory(),
            notificationWriter ?? new FakeNotificationWriter(),
            NullLogger<GenerateDueProbationReviewsJob>.Instance);

    private static ProbationDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ProbationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
