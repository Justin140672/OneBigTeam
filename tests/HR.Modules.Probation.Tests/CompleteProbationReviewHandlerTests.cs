using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Features.CompleteProbationReview;
using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Tests;

public class CompleteProbationReviewHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Completes_ManagerCheckIn_Without_Outcome()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var completedBy = Guid.NewGuid();

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.ManagerCheckIn);

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow))
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                CompletedByEmployeeId = completedBy,
                Notes = "All targets met."
            }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Completed", result.Value!.Status);
        Assert.Equal(Now, result.Value.CompletedAt);
        Assert.Equal(completedBy, result.Value.CompletedByEmployeeId);
        Assert.Equal("All targets met.", result.Value.Notes);

        var persistedRecord = await context.ProbationRecords.SingleAsync();
        Assert.Equal(ProbationStatus.Active, persistedRecord.Status);
    }

    [Fact]
    public async Task HandleAsync_Completes_HrReview_Without_Outcome()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.HrReview);

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow))
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                CompletedByEmployeeId = Guid.NewGuid()
            }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Completed", result.Value!.Status);

        var persistedRecord = await context.ProbationRecords.SingleAsync();
        Assert.Equal(ProbationStatus.Active, persistedRecord.Status);
    }

    [Fact]
    public async Task HandleAsync_Completes_FinalDecision_With_Pass_Outcome()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var completedBy = Guid.NewGuid();
        var decisionDate = new DateOnly(2026, 9, 1);

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow))
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                CompletedByEmployeeId = completedBy,
                Notes = "Excellent performance.",
                Outcome = ProbationOutcome.Pass,
                DecisionDate = decisionDate
            }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Completed", result.Value!.Status);

        var persistedRecord = await context.ProbationRecords.SingleAsync();
        Assert.Equal(ProbationStatus.Passed, persistedRecord.Status);
        Assert.Equal(completedBy, persistedRecord.DecisionMakerEmployeeId);
        Assert.Equal(decisionDate, persistedRecord.DecisionDate);
        Assert.Equal("Excellent performance.", persistedRecord.OutcomeNotes);
    }

    [Fact]
    public async Task HandleAsync_Completes_FinalDecision_With_Fail_Outcome()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var completedBy = Guid.NewGuid();
        var decisionDate = new DateOnly(2026, 9, 1);

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow))
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                CompletedByEmployeeId = completedBy,
                Notes = "Did not meet targets.",
                Outcome = ProbationOutcome.Fail,
                DecisionDate = decisionDate
            }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Completed", result.Value!.Status);

        var persistedRecord = await context.ProbationRecords.SingleAsync();
        Assert.Equal(ProbationStatus.Failed, persistedRecord.Status);
        Assert.Equal(completedBy, persistedRecord.DecisionMakerEmployeeId);
        Assert.Equal(decisionDate, persistedRecord.DecisionDate);
    }

    [Fact]
    public async Task HandleAsync_Completes_ExtensionConfirmation_With_Extend_Outcome()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var completedBy = Guid.NewGuid();
        var decisionDate = new DateOnly(2026, 9, 1);
        var newEndDate = new DateOnly(2026, 12, 1);

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.ExtensionConfirmation);

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow))
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                CompletedByEmployeeId = completedBy,
                Notes = "Needs more time.",
                Outcome = ProbationOutcome.Extend,
                DecisionDate = decisionDate,
                NewExpectedEndDate = newEndDate,
                ExtensionReason = "Did not meet all targets yet."
            }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Completed", result.Value!.Status);

        var persistedRecord = await context.ProbationRecords.SingleAsync();
        Assert.Equal(ProbationStatus.Extended, persistedRecord.Status);
        Assert.Equal(newEndDate, persistedRecord.ExpectedEndDate);
        Assert.Equal("Did not meet all targets yet.", persistedRecord.ExtensionReason);
        Assert.Equal(completedBy, persistedRecord.DecisionMakerEmployeeId);
        Assert.Equal(decisionDate, persistedRecord.DecisionDate);
    }

    [Fact]
    public async Task HandleAsync_Returns_ValidationError_When_FinalDecision_Has_No_Outcome()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow))
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                CompletedByEmployeeId = Guid.NewGuid()
            }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Completes_FinalDecision_With_Extend_Outcome()
    {
        await using var context = BuildContext();
        var companyId   = Guid.NewGuid();
        var completedBy = Guid.NewGuid();
        var newEndDate  = new DateOnly(2026, 12, 1);

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow))
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                CompletedByEmployeeId = completedBy,
                Outcome = ProbationOutcome.Extend,
                DecisionDate = new DateOnly(2026, 9, 1),
                NewExpectedEndDate = newEndDate,
                ExtensionReason = "Needs more time."
            }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Completed", result.Value!.Status);

        var savedRecord = await context.ProbationRecords.SingleAsync();
        Assert.Equal(ProbationStatus.Extended, savedRecord.Status);
        Assert.Equal(newEndDate, savedRecord.ExpectedEndDate);
        Assert.Equal("Needs more time.", savedRecord.ExtensionReason);
    }

    [Fact]
    public async Task HandleAsync_Returns_ValidationError_When_ExtensionConfirmation_Has_No_Outcome()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.ExtensionConfirmation);

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow))
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                CompletedByEmployeeId = Guid.NewGuid()
            }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_ValidationError_When_ExtensionConfirmation_Has_Pass_Outcome()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.ExtensionConfirmation);

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow))
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                CompletedByEmployeeId = Guid.NewGuid(),
                Outcome = ProbationOutcome.Pass,
                DecisionDate = new DateOnly(2026, 9, 1)
            }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_ValidationError_When_Outcome_Set_On_ManagerCheckIn()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.ManagerCheckIn);

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow))
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                CompletedByEmployeeId = Guid.NewGuid(),
                Outcome = ProbationOutcome.Pass,
                DecisionDate = new DateOnly(2026, 9, 1)
            }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_ValidationError_When_Review_Already_Completed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.ManagerCheckIn);
        review.Complete(Guid.NewGuid(), null, null, Now);
        await context.SaveChangesAsync();

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow))
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                CompletedByEmployeeId = Guid.NewGuid()
            }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_ProbationRecord_Does_Not_Exist()
    {
        await using var context = BuildContext();

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow))
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = Guid.NewGuid(),
                ProbationRecordId = Guid.NewGuid(),
                ReviewId = Guid.NewGuid(),
                CompletedByEmployeeId = Guid.NewGuid()
            }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Record_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();

        var record = ProbationRecord.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, Now);
        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow))
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = Guid.NewGuid(),
                ProbationRecordId = record.Id,
                ReviewId = Guid.NewGuid(),
                CompletedByEmployeeId = Guid.NewGuid()
            }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Review_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, Now);
        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow))
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = Guid.NewGuid(),
                CompletedByEmployeeId = Guid.NewGuid()
            }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Review_Belongs_To_Different_Record()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var record1 = ProbationRecord.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, Now);
        var record2 = ProbationRecord.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, Now);
        context.ProbationRecords.AddRange(record1, record2);

        var review = ProbationReview.Create(
            Guid.NewGuid(), companyId, record2.Id, ProbationReviewType.ManagerCheckIn,
            new DateOnly(2026, 7, 1), Now);
        context.ProbationReviews.Add(review);
        await context.SaveChangesAsync();

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow))
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record1.Id,
                ReviewId = review.Id,
                CompletedByEmployeeId = Guid.NewGuid()
            }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static async Task<(ProbationRecord record, ProbationReview review)> SeedRecordAndReview(
        ProbationDbContext context,
        Guid companyId,
        ProbationReviewType reviewType)
    {
        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, Now);
        context.ProbationRecords.Add(record);

        var review = ProbationReview.Create(
            Guid.NewGuid(), companyId, record.Id, reviewType,
            new DateOnly(2026, 7, 1), Now);
        context.ProbationReviews.Add(review);

        await context.SaveChangesAsync();
        return (record, review);
    }

    private static ProbationDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ProbationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
