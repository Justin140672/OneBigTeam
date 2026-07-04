using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.GetReturnToWorkReview;
using HR.Modules.Sickness.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests;

public class GetReturnToWorkReviewHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly DueDate = new(2026, 6, 20);

    private static SicknessDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<SicknessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task HandleAsync_Returns_Review_When_Found()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var recordId = Guid.NewGuid();

        var review = ReturnToWorkReview.Create(Guid.NewGuid(), companyId, recordId, employeeId, DueDate, Now);
        db.ReturnToWorkReviews.Add(review);
        await db.SaveChangesAsync();

        var handler = new GetReturnToWorkReviewHandler(db);
        var result = await handler.HandleAsync(
            new GetReturnToWorkReviewRequest { CompanyId = companyId, ReviewId = review.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(review.Id, result.Value!.Id);
        Assert.Equal(employeeId, result.Value.EmployeeId);
        Assert.Equal(recordId, result.Value.SicknessRecordId);
        Assert.Equal(DueDate, result.Value.DueDate);
        Assert.Equal("Pending", result.Value.Status);
        Assert.Null(result.Value.CompletedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Review_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var handler = new GetReturnToWorkReviewHandler(db);

        var result = await handler.HandleAsync(
            new GetReturnToWorkReviewRequest { CompanyId = Guid.NewGuid(), ReviewId = Guid.NewGuid() }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Review_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var review = ReturnToWorkReview.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DueDate, Now);
        db.ReturnToWorkReviews.Add(review);
        await db.SaveChangesAsync();

        var handler = new GetReturnToWorkReviewHandler(db);
        var result = await handler.HandleAsync(
            new GetReturnToWorkReviewRequest { CompanyId = Guid.NewGuid(), ReviewId = review.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }
}
