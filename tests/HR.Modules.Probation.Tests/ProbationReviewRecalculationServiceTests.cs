using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Services;
using HR.Modules.Probation.Tests.Infrastructure;
using HR.Modules.Tasks.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Tests;

public class ProbationReviewRecalculationServiceTests
{
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateTimeOffset SeedNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RecalcNow = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecalculateAsync_Cancels_Old_Pending_Reviews_And_Creates_New_Schedule()
    {
        await using var context = BuildContext();
        var record = await SeedRecord(context, expectedEndDate: new DateOnly(2026, 4, 1));

        var oldManagerCheckIn = ProbationReview.Create(
            Guid.NewGuid(), record.CompanyId, record.Id,
            ProbationReviewType.ManagerCheckIn, StartDate.AddDays(30), SeedNow);
        var oldFinalDecision = ProbationReview.Create(
            Guid.NewGuid(), record.CompanyId, record.Id,
            ProbationReviewType.FinalDecision, new DateOnly(2026, 4, 1), SeedNow);
        context.ProbationReviews.AddRange(oldManagerCheckIn, oldFinalDecision);
        await context.SaveChangesAsync();

        UpdateExpectedEndDate(record, new DateOnly(2026, 6, 1));
        await context.SaveChangesAsync();

        var service = BuildService(context);
        await service.RecalculateAsync(record, [30, 60, 90], RecalcNow, CancellationToken.None);

        var allReviews = await context.ProbationReviews.ToListAsync();

        var reloadedOldManagerCheckIn = allReviews.Single(r => r.Id == oldManagerCheckIn.Id);
        Assert.Equal(ProbationReviewStatus.Cancelled, reloadedOldManagerCheckIn.Status);
        Assert.NotNull(reloadedOldManagerCheckIn.SupersededByReviewId);

        var reloadedOldFinalDecision = allReviews.Single(r => r.Id == oldFinalDecision.Id);
        Assert.Equal(ProbationReviewStatus.Cancelled, reloadedOldFinalDecision.Status);
        Assert.NotNull(reloadedOldFinalDecision.SupersededByReviewId);

        var newManagerCheckIn = allReviews.Single(r =>
            r.Id == reloadedOldManagerCheckIn.SupersededByReviewId);
        Assert.Equal(ProbationReviewStatus.Pending, newManagerCheckIn.Status);
        Assert.Equal(StartDate.AddDays(30), newManagerCheckIn.DueDate);

        var newFinalDecision = allReviews.Single(r =>
            r.Id == reloadedOldFinalDecision.SupersededByReviewId);
        Assert.Equal(ProbationReviewStatus.Pending, newFinalDecision.Status);
        Assert.Equal(new DateOnly(2026, 6, 1), newFinalDecision.DueDate);

        var newHrReview = allReviews.Single(r =>
            r.ReviewType == ProbationReviewType.HrReview && r.Status == ProbationReviewStatus.Pending);
        Assert.Equal(StartDate.AddDays(60), newHrReview.DueDate);
    }

    [Fact]
    public async Task RecalculateAsync_Leaves_Completed_Reviews_Untouched()
    {
        await using var context = BuildContext();
        var record = await SeedRecord(context, expectedEndDate: new DateOnly(2026, 4, 1));

        var completedManagerCheckIn = ProbationReview.Create(
            Guid.NewGuid(), record.CompanyId, record.Id,
            ProbationReviewType.ManagerCheckIn, StartDate.AddDays(30), SeedNow);
        var completedByEmployeeId = Guid.NewGuid();
        completedManagerCheckIn.Complete(completedByEmployeeId, ProbationOutcome.Pass, "All good.", SeedNow);
        context.ProbationReviews.Add(completedManagerCheckIn);
        await context.SaveChangesAsync();

        UpdateExpectedEndDate(record, new DateOnly(2026, 6, 1));
        await context.SaveChangesAsync();

        var service = BuildService(context);
        await service.RecalculateAsync(record, [30, 60, 90], RecalcNow, CancellationToken.None);

        var reloaded = await context.ProbationReviews.SingleAsync(r => r.Id == completedManagerCheckIn.Id);
        Assert.Equal(ProbationReviewStatus.Completed, reloaded.Status);
        Assert.Equal(completedByEmployeeId, reloaded.CompletedByEmployeeId);
        Assert.Equal(ProbationOutcome.Pass, reloaded.Outcome);
        Assert.Equal("All good.", reloaded.Notes);
        Assert.Null(reloaded.SupersededByReviewId);

        // A fresh ManagerCheckIn should still be created for the new schedule — the completed one
        // is historical and not a substitute for the recalculated pending review.
        Assert.Contains(
            await context.ProbationReviews.Where(r => r.Status == ProbationReviewStatus.Pending).ToListAsync(),
            r => r.ReviewType == ProbationReviewType.ManagerCheckIn);
    }

    [Fact]
    public async Task RecalculateAsync_Creates_Task_For_Every_New_Review_And_Cancels_Task_For_Every_Superseded_Review()
    {
        await using var context = BuildContext();
        var record = await SeedRecord(context, expectedEndDate: new DateOnly(2026, 4, 1));

        var oldManagerCheckIn = ProbationReview.Create(
            Guid.NewGuid(), record.CompanyId, record.Id,
            ProbationReviewType.ManagerCheckIn, StartDate.AddDays(30), SeedNow);
        var oldHrReview = ProbationReview.Create(
            Guid.NewGuid(), record.CompanyId, record.Id,
            ProbationReviewType.HrReview, StartDate.AddDays(60), SeedNow);
        var oldFinalDecision = ProbationReview.Create(
            Guid.NewGuid(), record.CompanyId, record.Id,
            ProbationReviewType.FinalDecision, new DateOnly(2026, 4, 1), SeedNow);
        context.ProbationReviews.AddRange(oldManagerCheckIn, oldHrReview, oldFinalDecision);
        await context.SaveChangesAsync();

        UpdateExpectedEndDate(record, new DateOnly(2026, 6, 1));
        await context.SaveChangesAsync();

        var taskCreator = new FakeTaskCreator();
        var taskCanceller = new FakeTaskCanceller();
        var service = new ProbationReviewRecalculationService(
            context, taskCreator, taskCanceller, new FakeEmployeeNameReader());

        await service.RecalculateAsync(record, [30, 60, 90], RecalcNow, CancellationToken.None);

        Assert.Equal(3, taskCreator.Created.Count);
        Assert.All(taskCreator.Created, t =>
        {
            Assert.Equal(record.ManagerEmployeeId, t.AssignedEmployeeId);
            Assert.False(t.NotifyAssignee);
        });

        Assert.Equal(3, taskCanceller.Calls.Count);
        var cancelledSourceIds = taskCanceller.Calls.Select(c => c.SourceEntityId).ToList();
        Assert.Contains(oldManagerCheckIn.Id, cancelledSourceIds);
        Assert.Contains(oldHrReview.Id, cancelledSourceIds);
        Assert.Contains(oldFinalDecision.Id, cancelledSourceIds);
        Assert.All(taskCanceller.Calls, c =>
        {
            Assert.Equal(TaskSource.Probation, c.Source);
            Assert.Equal(TaskActionType.Review, c.ActionType);
        });
    }

    [Fact]
    public async Task RecalculateAsync_Cancels_Stale_Pending_Review_When_New_Schedule_Drops_That_Type()
    {
        await using var context = BuildContext();
        // Original schedule has both ManagerCheckIn and HrReview.
        var record = await SeedRecord(context, expectedEndDate: new DateOnly(2026, 4, 1));

        var oldManagerCheckIn = ProbationReview.Create(
            Guid.NewGuid(), record.CompanyId, record.Id,
            ProbationReviewType.ManagerCheckIn, StartDate.AddDays(30), SeedNow);
        var oldHrReview = ProbationReview.Create(
            Guid.NewGuid(), record.CompanyId, record.Id,
            ProbationReviewType.HrReview, StartDate.AddDays(60), SeedNow);
        context.ProbationReviews.AddRange(oldManagerCheckIn, oldHrReview);
        await context.SaveChangesAsync();

        // End date moves much closer: only ManagerCheckIn (day 30) survives + FinalDecision.
        UpdateExpectedEndDate(record, StartDate.AddDays(40));
        await context.SaveChangesAsync();

        var service = BuildService(context);
        await service.RecalculateAsync(record, [30, 60, 90], RecalcNow, CancellationToken.None);

        var reloadedOldHrReview = await context.ProbationReviews.SingleAsync(r => r.Id == oldHrReview.Id);
        Assert.Equal(ProbationReviewStatus.Cancelled, reloadedOldHrReview.Status);
        Assert.Null(reloadedOldHrReview.SupersededByReviewId); // no replacement of the same type

        Assert.DoesNotContain(
            await context.ProbationReviews.ToListAsync(),
            r => r.ReviewType == ProbationReviewType.HrReview && r.Status == ProbationReviewStatus.Pending);
    }

    private static void UpdateExpectedEndDate(ProbationRecord record, DateOnly expectedEndDate) =>
        record.Update(
            record.ManagerEmployeeId,
            expectedEndDate,
            record.Status,
            record.Notes,
            record.ExtensionReason,
            record.DecisionMakerEmployeeId,
            record.DecisionDate,
            record.OutcomeNotes,
            RecalcNow);

    private static ProbationReviewRecalculationService BuildService(ProbationDbContext context) =>
        new(context, new FakeTaskCreator(), new FakeTaskCanceller(), new FakeEmployeeNameReader());

    private static async Task<ProbationRecord> SeedRecord(
        ProbationDbContext context, DateOnly expectedEndDate)
    {
        var record = ProbationRecord.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            StartDate, expectedEndDate, null, SeedNow);
        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();
        return record;
    }

    private static ProbationDbContext BuildContext() =>
        new(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ProbationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
