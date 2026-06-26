using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Features.CompleteProbationReviewFromTask;
using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Tests.Infrastructure;
using HR.SharedKernel;
using HR.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Tests;

public class CompleteProbationReviewFromTaskActionTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 6, 25);

    [Fact]
    public void Source_Is_ProbationReview()
    {
        using var context = BuildContext();
        var action = new CompleteProbationReviewFromTaskAction(context, new FakeClock(FixedUtcNow));

        Assert.Equal(TaskSource.ProbationReview, action.Source);
    }

    [Fact]
    public async Task ExecuteAsync_Completes_ManagerCheckIn_Review()
    {
        await using var context = BuildContext();
        var companyId   = Guid.NewGuid();
        var completedBy = Guid.NewGuid();

        var (_, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.ManagerCheckIn);

        var taskContext = BuildContext(companyId, completedBy, review.Id, notes: "Good progress.");

        await new CompleteProbationReviewFromTaskAction(context, new FakeClock(FixedUtcNow))
            .ExecuteAsync(taskContext, CancellationToken.None);

        var saved = await context.ProbationReviews.SingleAsync();
        Assert.Equal(ProbationReviewStatus.Completed, saved.Status);
        Assert.Equal(completedBy, saved.CompletedByEmployeeId);
        Assert.Equal("Good progress.", saved.Notes);
        Assert.Equal(Now, saved.CompletedAt);
    }

    [Fact]
    public async Task ExecuteAsync_Completes_FinalDecision_With_Pass_And_Sets_Record_To_Passed()
    {
        await using var context = BuildContext();
        var companyId   = Guid.NewGuid();
        var completedBy = Guid.NewGuid();

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);

        var taskContext = BuildContext(companyId, completedBy, review.Id, outcomeDecision: "Pass", notes: "Excellent.");

        await new CompleteProbationReviewFromTaskAction(context, new FakeClock(FixedUtcNow))
            .ExecuteAsync(taskContext, CancellationToken.None);

        var savedRecord = await context.ProbationRecords.SingleAsync();
        Assert.Equal(ProbationStatus.Passed, savedRecord.Status);
        Assert.Equal(completedBy, savedRecord.DecisionMakerEmployeeId);
        Assert.Equal(Today, savedRecord.DecisionDate);
        Assert.Equal("Excellent.", savedRecord.OutcomeNotes);

        var savedReview = await context.ProbationReviews.SingleAsync();
        Assert.Equal(ProbationReviewStatus.Completed, savedReview.Status);
    }

    [Fact]
    public async Task ExecuteAsync_Completes_FinalDecision_With_Fail_And_Sets_Record_To_Failed()
    {
        await using var context = BuildContext();
        var companyId   = Guid.NewGuid();
        var completedBy = Guid.NewGuid();

        var (_, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);

        var taskContext = BuildContext(companyId, completedBy, review.Id, outcomeDecision: "Fail", notes: "Did not meet targets.");

        await new CompleteProbationReviewFromTaskAction(context, new FakeClock(FixedUtcNow))
            .ExecuteAsync(taskContext, CancellationToken.None);

        var savedRecord = await context.ProbationRecords.SingleAsync();
        Assert.Equal(ProbationStatus.Failed, savedRecord.Status);
        Assert.Equal(Today, savedRecord.DecisionDate);

        var savedReview = await context.ProbationReviews.SingleAsync();
        Assert.Equal(ProbationReviewStatus.Completed, savedReview.Status);
    }

    [Fact]
    public async Task ExecuteAsync_Completes_FinalDecision_With_Extend_And_Sets_Record_To_Extended()
    {
        await using var context = BuildContext();
        var companyId   = Guid.NewGuid();
        var completedBy = Guid.NewGuid();
        var newEndDate  = new DateOnly(2026, 10, 7);

        var (_, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);

        var taskContext = BuildContext(companyId, completedBy, review.Id,
            outcomeDecision: $"Extend|{newEndDate:yyyy-MM-dd}",
            notes: "Needs more time to demonstrate improvement.");

        await new CompleteProbationReviewFromTaskAction(context, new FakeClock(FixedUtcNow))
            .ExecuteAsync(taskContext, CancellationToken.None);

        var savedRecord = await context.ProbationRecords.SingleAsync();
        Assert.Equal(ProbationStatus.Extended, savedRecord.Status);
        Assert.Equal(newEndDate, savedRecord.ExpectedEndDate);
        Assert.Equal(completedBy, savedRecord.DecisionMakerEmployeeId);
        Assert.Equal(Today, savedRecord.DecisionDate);
        Assert.Equal("Needs more time to demonstrate improvement.", savedRecord.ExtensionReason);

        var savedReview = await context.ProbationReviews.SingleAsync();
        Assert.Equal(ProbationReviewStatus.Completed, savedReview.Status);
    }

    [Fact]
    public async Task ExecuteAsync_Completes_HrReview_Without_Outcome()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var (_, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.HrReview);

        var taskContext = BuildContext(companyId, Guid.NewGuid(), review.Id);

        await new CompleteProbationReviewFromTaskAction(context, new FakeClock(FixedUtcNow))
            .ExecuteAsync(taskContext, CancellationToken.None);

        var savedRecord = await context.ProbationRecords.SingleAsync();
        Assert.Equal(ProbationStatus.Active, savedRecord.Status);

        var savedReview = await context.ProbationReviews.SingleAsync();
        Assert.Equal(ProbationReviewStatus.Completed, savedReview.Status);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Nothing_When_SourceEntityId_Is_Null()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        await SeedRecordAndReview(context, companyId, ProbationReviewType.ManagerCheckIn);

        var taskContext = BuildContext(companyId, Guid.NewGuid(), sourceEntityId: null);

        await new CompleteProbationReviewFromTaskAction(context, new FakeClock(FixedUtcNow))
            .ExecuteAsync(taskContext, CancellationToken.None);

        var review = await context.ProbationReviews.SingleAsync();
        Assert.Equal(ProbationReviewStatus.Pending, review.Status);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Nothing_When_Review_Not_Found()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        await SeedRecordAndReview(context, companyId, ProbationReviewType.ManagerCheckIn);

        var taskContext = BuildContext(companyId, Guid.NewGuid(), sourceEntityId: Guid.NewGuid());

        await new CompleteProbationReviewFromTaskAction(context, new FakeClock(FixedUtcNow))
            .ExecuteAsync(taskContext, CancellationToken.None);

        var review = await context.ProbationReviews.SingleAsync();
        Assert.Equal(ProbationReviewStatus.Pending, review.Status);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Nothing_When_Review_Already_Completed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var (_, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.ManagerCheckIn);
        review.Complete(Guid.NewGuid(), null, null, Now);
        await context.SaveChangesAsync();

        var completedBy = Guid.NewGuid();
        var taskContext = BuildContext(companyId, completedBy, review.Id, notes: "Late completion.");

        await new CompleteProbationReviewFromTaskAction(context, new FakeClock(FixedUtcNow))
            .ExecuteAsync(taskContext, CancellationToken.None);

        var saved = await context.ProbationReviews.SingleAsync();
        Assert.NotEqual(completedBy, saved.CompletedByEmployeeId);
    }

    private static TaskCompletionContext BuildContext(
        Guid companyId,
        Guid completedBy,
        Guid? sourceEntityId,
        string? outcomeDecision = null,
        string? notes = null) =>
        new(companyId,
            Guid.NewGuid(),
            "Complete probation review — Test Employee",
            null,
            TaskSource.ProbationReview,
            null,
            completedBy,
            Now,
            sourceEntityId,
            outcomeDecision,
            notes);

    private static async Task<(ProbationRecord record, ProbationReview review)> SeedRecordAndReview(
        ProbationDbContext context,
        Guid companyId,
        ProbationReviewType reviewType)
    {
        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 4, 1), new DateOnly(2026, 7, 1), null, Now);
        context.ProbationRecords.Add(record);

        var review = ProbationReview.Create(
            Guid.NewGuid(), companyId, record.Id, reviewType,
            new DateOnly(2026, 5, 1), Now);
        context.ProbationReviews.Add(review);

        await context.SaveChangesAsync();
        return (record, review);
    }

    private static ProbationDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ProbationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
