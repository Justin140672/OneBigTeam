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
    public async Task HandleAsync_Completes_Pending_Review()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var completedBy = Guid.NewGuid();

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, Now);
        context.ProbationRecords.Add(record);

        var review = ProbationReview.Create(
            Guid.NewGuid(), companyId, record.Id, ProbationReviewType.ManagerCheckIn,
            new DateOnly(2026, 7, 1), Now);
        context.ProbationReviews.Add(review);

        await context.SaveChangesAsync();

        var handler = new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new CompleteProbationReviewRequest
        {
            CompanyId = companyId,
            ProbationRecordId = record.Id,
            ReviewId = review.Id,
            CompletedByEmployeeId = completedBy,
            Notes = "All targets met."
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(review.Id, result.Value!.Id);
        Assert.Equal(companyId, result.Value.CompanyId);
        Assert.Equal(record.Id, result.Value.ProbationRecordId);
        Assert.Equal("ManagerCheckIn", result.Value.ReviewType);
        Assert.Equal("Completed", result.Value.Status);
        Assert.Equal(Now, result.Value.CompletedAt);
        Assert.Equal(completedBy, result.Value.CompletedByEmployeeId);
        Assert.Equal("All targets met.", result.Value.Notes);

        var persisted = await context.ProbationReviews.SingleAsync();
        Assert.Equal(ProbationReviewStatus.Completed, persisted.Status);
        Assert.Equal(completedBy, persisted.CompletedByEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Completes_Review_With_Null_Notes()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, Now);
        context.ProbationRecords.Add(record);

        var review = ProbationReview.Create(
            Guid.NewGuid(), companyId, record.Id, ProbationReviewType.HrReview,
            new DateOnly(2026, 8, 1), Now);
        context.ProbationReviews.Add(review);

        await context.SaveChangesAsync();

        var handler = new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new CompleteProbationReviewRequest
        {
            CompanyId = companyId,
            ProbationRecordId = record.Id,
            ReviewId = review.Id,
            CompletedByEmployeeId = Guid.NewGuid(),
            Notes = null
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Completed", result.Value!.Status);
        Assert.Null(result.Value.Notes);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_ProbationRecord_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new CompleteProbationReviewRequest
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

        var handler = new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new CompleteProbationReviewRequest
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

        var handler = new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new CompleteProbationReviewRequest
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

        var handler = new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new CompleteProbationReviewRequest
        {
            CompanyId = companyId,
            ProbationRecordId = record1.Id,
            ReviewId = review.Id,
            CompletedByEmployeeId = Guid.NewGuid()
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_ValidationError_When_Review_Already_Completed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var completedBy = Guid.NewGuid();

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, Now);
        context.ProbationRecords.Add(record);

        var review = ProbationReview.Create(
            Guid.NewGuid(), companyId, record.Id, ProbationReviewType.ManagerCheckIn,
            new DateOnly(2026, 7, 1), Now);
        review.Complete(completedBy, "Initial completion.", Now);
        context.ProbationReviews.Add(review);

        await context.SaveChangesAsync();

        var handler = new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new CompleteProbationReviewRequest
        {
            CompanyId = companyId,
            ProbationRecordId = record.Id,
            ReviewId = review.Id,
            CompletedByEmployeeId = Guid.NewGuid(),
            Notes = "Second attempt."
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    private static ProbationDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ProbationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
