using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Features.UpdateProbationRecord;
using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Services;
using HR.Modules.Probation.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Tests;

public class UpdateProbationRecordHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 25, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Updates_Manager_And_Notes()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var newManagerId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, now);
        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();

        var handler = new UpdateProbationRecordHandler(
            context,
            new FakeClock(FixedUtcNow),
            new ProbationReviewRecalculationService(
                context, new FakeTaskCreator(), new FakeTaskCanceller(), new FakeEmployeeNameReader(),
                new FakeHrAdministratorDirectory(), new FakeNotificationWriter()),
            new FakeCompanyProbationSettingsReader());

        var result = await handler.HandleAsync(new UpdateProbationRecordRequest
        {
            CompanyId = companyId,
            Id = record.Id,
            ManagerEmployeeId = newManagerId,
            ExpectedEndDate = new DateOnly(2026, 9, 1),
            Status = "Active",
            Notes = "Updated notes."
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(newManagerId, result.Value!.ManagerEmployeeId);
        Assert.Equal("Active", result.Value.Status);
        Assert.Equal("Updated notes.", result.Value.Notes);
    }

    [Fact]
    public async Task HandleAsync_Transitions_To_Extended()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), managerId,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, now);
        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();

        var handler = new UpdateProbationRecordHandler(
            context,
            new FakeClock(FixedUtcNow),
            new ProbationReviewRecalculationService(
                context, new FakeTaskCreator(), new FakeTaskCanceller(), new FakeEmployeeNameReader(),
                new FakeHrAdministratorDirectory(), new FakeNotificationWriter()),
            new FakeCompanyProbationSettingsReader());

        var result = await handler.HandleAsync(new UpdateProbationRecordRequest
        {
            CompanyId = companyId,
            Id = record.Id,
            ManagerEmployeeId = managerId,
            ExpectedEndDate = new DateOnly(2026, 12, 1),
            Status = "Extended",
            ExtensionReason = "Needs more time to meet targets."
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Extended", result.Value!.Status);
        Assert.Equal(new DateOnly(2026, 12, 1), result.Value.ExpectedEndDate);
        Assert.Equal("Needs more time to meet targets.", result.Value.ExtensionReason);
    }

    [Fact]
    public async Task HandleAsync_Transitions_To_Passed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), managerId,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, now);
        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();

        var handler = new UpdateProbationRecordHandler(
            context,
            new FakeClock(FixedUtcNow),
            new ProbationReviewRecalculationService(
                context, new FakeTaskCreator(), new FakeTaskCanceller(), new FakeEmployeeNameReader(),
                new FakeHrAdministratorDirectory(), new FakeNotificationWriter()),
            new FakeCompanyProbationSettingsReader());

        var result = await handler.HandleAsync(new UpdateProbationRecordRequest
        {
            CompanyId = companyId,
            Id = record.Id,
            ManagerEmployeeId = managerId,
            ExpectedEndDate = new DateOnly(2026, 9, 1),
            Status = "Passed",
            DecisionMakerEmployeeId = managerId,
            DecisionDate = new DateOnly(2026, 9, 1),
            OutcomeNotes = "Excellent performance."
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Passed", result.Value!.Status);
        Assert.Equal(managerId, result.Value.DecisionMakerEmployeeId);
        Assert.Equal(new DateOnly(2026, 9, 1), result.Value.DecisionDate);
        Assert.Equal("Excellent performance.", result.Value.OutcomeNotes);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Unknown_Record()
    {
        await using var context = BuildContext();
        var handler = new UpdateProbationRecordHandler(
            context,
            new FakeClock(FixedUtcNow),
            new ProbationReviewRecalculationService(
                context, new FakeTaskCreator(), new FakeTaskCanceller(), new FakeEmployeeNameReader(),
                new FakeHrAdministratorDirectory(), new FakeNotificationWriter()),
            new FakeCompanyProbationSettingsReader());

        var result = await handler.HandleAsync(new UpdateProbationRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            ManagerEmployeeId = Guid.NewGuid(),
            ExpectedEndDate = new DateOnly(2026, 9, 1),
            Status = "Active"
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Trims_Notes()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), managerId,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, now);
        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();

        var handler = new UpdateProbationRecordHandler(
            context,
            new FakeClock(FixedUtcNow),
            new ProbationReviewRecalculationService(
                context, new FakeTaskCreator(), new FakeTaskCanceller(), new FakeEmployeeNameReader(),
                new FakeHrAdministratorDirectory(), new FakeNotificationWriter()),
            new FakeCompanyProbationSettingsReader());

        var result = await handler.HandleAsync(new UpdateProbationRecordRequest
        {
            CompanyId = companyId,
            Id = record.Id,
            ManagerEmployeeId = managerId,
            ExpectedEndDate = new DateOnly(2026, 9, 1),
            Status = "Active",
            Notes = "  Trimmed.  "
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Trimmed.", result.Value!.Notes);
    }

    [Fact]
    public async Task HandleAsync_Changing_ExpectedEndDate_Triggers_Recalculation()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), managerId,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, now);
        context.ProbationRecords.Add(record);

        var oldFinalDecision = ProbationReview.Create(
            Guid.NewGuid(), companyId, record.Id,
            ProbationReviewType.FinalDecision, new DateOnly(2026, 9, 1), now);
        context.ProbationReviews.Add(oldFinalDecision);
        await context.SaveChangesAsync();

        var handler = new UpdateProbationRecordHandler(
            context,
            new FakeClock(FixedUtcNow),
            new ProbationReviewRecalculationService(
                context, new FakeTaskCreator(), new FakeTaskCanceller(), new FakeEmployeeNameReader(),
                new FakeHrAdministratorDirectory(), new FakeNotificationWriter()),
            new FakeCompanyProbationSettingsReader());

        var result = await handler.HandleAsync(new UpdateProbationRecordRequest
        {
            CompanyId = companyId,
            Id = record.Id,
            ManagerEmployeeId = managerId,
            ExpectedEndDate = new DateOnly(2026, 12, 1),
            Status = "Active"
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var reloadedOldFinalDecision = await context.ProbationReviews.SingleAsync(r => r.Id == oldFinalDecision.Id);
        Assert.Equal(ProbationReviewStatus.Cancelled, reloadedOldFinalDecision.Status);

        var newFinalDecision = await context.ProbationReviews.SingleAsync(r =>
            r.Id != oldFinalDecision.Id && r.ReviewType == ProbationReviewType.FinalDecision);
        Assert.Equal(ProbationReviewStatus.Pending, newFinalDecision.Status);
        Assert.Equal(new DateOnly(2026, 12, 1), newFinalDecision.DueDate);
    }

    [Fact]
    public async Task HandleAsync_Unchanged_ExpectedEndDate_Does_Not_Trigger_Recalculation()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), managerId,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, now);
        context.ProbationRecords.Add(record);

        var existingFinalDecision = ProbationReview.Create(
            Guid.NewGuid(), companyId, record.Id,
            ProbationReviewType.FinalDecision, new DateOnly(2026, 9, 1), now);
        context.ProbationReviews.Add(existingFinalDecision);
        await context.SaveChangesAsync();

        var taskCreator = new FakeTaskCreator();
        var handler = new UpdateProbationRecordHandler(
            context,
            new FakeClock(FixedUtcNow),
            new ProbationReviewRecalculationService(
                context, taskCreator, new FakeTaskCanceller(), new FakeEmployeeNameReader(),
                new FakeHrAdministratorDirectory(), new FakeNotificationWriter()),
            new FakeCompanyProbationSettingsReader());

        var result = await handler.HandleAsync(new UpdateProbationRecordRequest
        {
            CompanyId = companyId,
            Id = record.Id,
            ManagerEmployeeId = managerId,
            ExpectedEndDate = new DateOnly(2026, 9, 1), // unchanged
            Status = "Active",
            Notes = "No date change."
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(taskCreator.Created);

        var reviews = await context.ProbationReviews.ToListAsync();
        var onlyReview = Assert.Single(reviews);
        Assert.Equal(existingFinalDecision.Id, onlyReview.Id);
        Assert.Equal(ProbationReviewStatus.Pending, onlyReview.Status);
    }

    private static ProbationDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ProbationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
