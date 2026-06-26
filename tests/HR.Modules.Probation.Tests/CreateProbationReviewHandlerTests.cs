using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Features.CreateProbationReview;
using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Tests;

public class CreateProbationReviewHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 25, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Creates_Review_For_Existing_Record()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, now);
        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();

        var handler = new CreateProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher());

        var result = await handler.HandleAsync(new CreateProbationReviewRequest
        {
            CompanyId = companyId,
            ProbationRecordId = record.Id,
            ReviewType = "ManagerCheckIn",
            DueDate = new DateOnly(2026, 7, 1)
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value!.Id);
        Assert.Equal(companyId, result.Value.CompanyId);
        Assert.Equal(record.Id, result.Value.ProbationRecordId);
        Assert.Equal("ManagerCheckIn", result.Value.ReviewType);
        Assert.Equal(new DateOnly(2026, 7, 1), result.Value.DueDate);
        Assert.Equal("Pending", result.Value.Status);
        Assert.Equal(now, result.Value.CreatedAt);

        Assert.Equal(1, await context.ProbationReviews.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_ProbationRecord_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new CreateProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher());

        var result = await handler.HandleAsync(new CreateProbationReviewRequest
        {
            CompanyId = Guid.NewGuid(),
            ProbationRecordId = Guid.NewGuid(),
            ReviewType = "HrReview",
            DueDate = new DateOnly(2026, 7, 1)
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Record_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var record = ProbationRecord.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, now);
        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();

        var handler = new CreateProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher());

        var result = await handler.HandleAsync(new CreateProbationReviewRequest
        {
            CompanyId = Guid.NewGuid(),
            ProbationRecordId = record.Id,
            ReviewType = "HrReview",
            DueDate = new DateOnly(2026, 7, 1)
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Multiple_Reviews_For_Same_Record()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, now);
        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();

        var handler = new CreateProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher());

        await handler.HandleAsync(new CreateProbationReviewRequest
        {
            CompanyId = companyId,
            ProbationRecordId = record.Id,
            ReviewType = "ManagerCheckIn",
            DueDate = new DateOnly(2026, 7, 1)
        }, CancellationToken.None);

        var result = await handler.HandleAsync(new CreateProbationReviewRequest
        {
            CompanyId = companyId,
            ProbationRecordId = record.Id,
            ReviewType = "HrReview",
            DueDate = new DateOnly(2026, 8, 1)
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, await context.ProbationReviews.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Review_Of_Same_Type_Already_Exists()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, now);
        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();

        var handler = new CreateProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher());

        await handler.HandleAsync(new CreateProbationReviewRequest
        {
            CompanyId         = companyId,
            ProbationRecordId = record.Id,
            ReviewType        = "ManagerCheckIn",
            DueDate           = new DateOnly(2026, 7, 1)
        }, CancellationToken.None);

        var duplicate = await handler.HandleAsync(new CreateProbationReviewRequest
        {
            CompanyId         = companyId,
            ProbationRecordId = record.Id,
            ReviewType        = "ManagerCheckIn",
            DueDate           = new DateOnly(2026, 7, 15)
        }, CancellationToken.None);

        Assert.True(duplicate.IsFailure);
        Assert.Equal("conflict", duplicate.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Completed_Review_Of_Same_Type_Exists()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, now);
        context.ProbationRecords.Add(record);

        var existing = ProbationReview.Create(
            Guid.NewGuid(), companyId, record.Id,
            ProbationReviewType.HrReview, new DateOnly(2026, 7, 1), now);
        existing.Complete(Guid.NewGuid(), null, null, now);
        context.ProbationReviews.Add(existing);
        await context.SaveChangesAsync();

        var handler = new CreateProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher());

        var result = await handler.HandleAsync(new CreateProbationReviewRequest
        {
            CompanyId         = companyId,
            ProbationRecordId = record.Id,
            ReviewType        = "HrReview",
            DueDate           = new DateOnly(2026, 8, 1)
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Different_Review_Types_For_Same_Record()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, now);
        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();

        var handler = new CreateProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher());

        var first = await handler.HandleAsync(new CreateProbationReviewRequest
        {
            CompanyId         = companyId,
            ProbationRecordId = record.Id,
            ReviewType        = "ManagerCheckIn",
            DueDate           = new DateOnly(2026, 7, 1)
        }, CancellationToken.None);

        var second = await handler.HandleAsync(new CreateProbationReviewRequest
        {
            CompanyId         = companyId,
            ProbationRecordId = record.Id,
            ReviewType        = "HrReview",
            DueDate           = new DateOnly(2026, 8, 1)
        }, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(2, await context.ProbationReviews.CountAsync());
    }

    private static ProbationDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ProbationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
