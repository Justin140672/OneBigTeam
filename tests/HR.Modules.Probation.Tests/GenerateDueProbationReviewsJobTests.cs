using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Jobs;
using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Tests.Infrastructure;
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
        existing.Complete(Guid.NewGuid(), null, SeedNow);
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
        FakeEmployeeNameReader? employeeNameReader = null) =>
        new(context,
            new FakeClock(today),
            taskCreator ?? new FakeTaskCreator(),
            employeeNameReader ?? new FakeEmployeeNameReader(),
            NullLogger<GenerateDueProbationReviewsJob>.Instance);

    private static ProbationDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ProbationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
