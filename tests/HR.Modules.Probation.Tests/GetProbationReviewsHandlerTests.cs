using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Features.GetProbationReviews;
using HR.Modules.Probation.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Tests;

public class GetProbationReviewsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_When_No_Reviews_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, DateOnly.FromDateTime(Now.UtcDateTime), Now);
        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();

        var handler = new GetProbationReviewsHandler(context);

        var result = await handler.HandleAsync(
            new GetProbationReviewsRequest { CompanyId = companyId, ProbationRecordId = record.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Returns_Reviews_Ordered_By_DueDate()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, DateOnly.FromDateTime(Now.UtcDateTime), Now);
        context.ProbationRecords.Add(record);

        context.ProbationReviews.AddRange(
            ProbationReview.Create(Guid.NewGuid(), companyId, record.Id, ProbationReviewType.FinalDecision, new DateOnly(2026, 9, 1), Now),
            ProbationReview.Create(Guid.NewGuid(), companyId, record.Id, ProbationReviewType.ManagerCheckIn, new DateOnly(2026, 7, 1), Now),
            ProbationReview.Create(Guid.NewGuid(), companyId, record.Id, ProbationReviewType.HrReview, new DateOnly(2026, 8, 1), Now));
        await context.SaveChangesAsync();

        var handler = new GetProbationReviewsHandler(context);

        var result = await handler.HandleAsync(
            new GetProbationReviewsRequest { CompanyId = companyId, ProbationRecordId = record.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Items.Count);
        Assert.Equal("ManagerCheckIn", result.Value.Items[0].ReviewType);
        Assert.Equal("HrReview", result.Value.Items[1].ReviewType);
        Assert.Equal("FinalDecision", result.Value.Items[2].ReviewType);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Unknown_ProbationRecord()
    {
        await using var context = BuildContext();
        var handler = new GetProbationReviewsHandler(context);

        var result = await handler.HandleAsync(
            new GetProbationReviewsRequest { CompanyId = Guid.NewGuid(), ProbationRecordId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Only_Returns_Reviews_For_Requested_Record()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var record1 = ProbationRecord.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, DateOnly.FromDateTime(Now.UtcDateTime), Now);
        var record2 = ProbationRecord.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, DateOnly.FromDateTime(Now.UtcDateTime), Now);
        context.ProbationRecords.AddRange(record1, record2);

        context.ProbationReviews.AddRange(
            ProbationReview.Create(Guid.NewGuid(), companyId, record1.Id, ProbationReviewType.ManagerCheckIn, new DateOnly(2026, 7, 1), Now),
            ProbationReview.Create(Guid.NewGuid(), companyId, record2.Id, ProbationReviewType.HrReview, new DateOnly(2026, 7, 1), Now));
        await context.SaveChangesAsync();

        var handler = new GetProbationReviewsHandler(context);

        var result = await handler.HandleAsync(
            new GetProbationReviewsRequest { CompanyId = companyId, ProbationRecordId = record1.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal(record1.Id, result.Value.Items[0].ProbationRecordId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Completed_Review_With_Completion_Details()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var completedBy = Guid.NewGuid();

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, DateOnly.FromDateTime(Now.UtcDateTime), Now);
        context.ProbationRecords.Add(record);

        var review = ProbationReview.Create(
            Guid.NewGuid(), companyId, record.Id, ProbationReviewType.ManagerCheckIn, new DateOnly(2026, 7, 1), Now);
        review.Complete(completedBy, null, "All targets met.", Now);
        context.ProbationReviews.Add(review);
        await context.SaveChangesAsync();

        var handler = new GetProbationReviewsHandler(context);

        var result = await handler.HandleAsync(
            new GetProbationReviewsRequest { CompanyId = companyId, ProbationRecordId = record.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("Completed", item.Status);
        Assert.Equal(completedBy, item.CompletedByEmployeeId);
        Assert.Equal("All targets met.", item.Notes);
        Assert.NotNull(item.CompletedAt);
    }

    private static ProbationDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ProbationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
