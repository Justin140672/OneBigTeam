using HR.Modules.Tasks.Contracts;
using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Features.CompleteProbationReviewFromTask;
using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Tests.Infrastructure;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Tests;

public class CompleteProbationReviewFromTaskActionTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 6, 25);

    [Fact]
    public void Source_Is_Probation()
    {
        using var context = BuildContext();
        var action = new CompleteProbationReviewFromTaskAction(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter());

        Assert.Equal(TaskSource.Probation, action.Source);
        Assert.Equal(TaskActionType.Review, action.ActionType);
    }

    [Fact]
    public async Task ExecuteAsync_Completes_ManagerCheckIn_Review()
    {
        await using var context = BuildContext();
        var companyId   = Guid.NewGuid();
        var completedBy = Guid.NewGuid();

        var (_, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.ManagerCheckIn);

        var taskContext = BuildContext(companyId, completedBy, review.Id, notes: "Good progress.");

        await new CompleteProbationReviewFromTaskAction(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
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

        await new CompleteProbationReviewFromTaskAction(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
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

        await new CompleteProbationReviewFromTaskAction(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
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

        await new CompleteProbationReviewFromTaskAction(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .ExecuteAsync(taskContext, CancellationToken.None);

        var savedRecord = await context.ProbationRecords.SingleAsync();
        Assert.Equal(ProbationStatus.Extended, savedRecord.Status);
        Assert.Equal(newEndDate, savedRecord.ExpectedEndDate);
        Assert.Equal(completedBy, savedRecord.DecisionMakerEmployeeId);
        Assert.Equal(Today, savedRecord.DecisionDate);
        Assert.Equal("Needs more time to demonstrate improvement.", savedRecord.ExtensionReason);

        var savedReview = await context.ProbationReviews.SingleAsync(r => r.Id == review.Id);
        Assert.Equal(ProbationReviewStatus.Completed, savedReview.Status);

        // Extending also schedules the PROB-01 follow-up cycle: an ExtensionConfirmation review
        // and a fresh Pending FinalDecision review for the new expected end date.
        var extensionConfirmation = await context.ProbationReviews
            .SingleAsync(r => r.ReviewType == ProbationReviewType.ExtensionConfirmation);
        Assert.Equal(ProbationReviewStatus.Pending, extensionConfirmation.Status);

        var newFinalReview = await context.ProbationReviews
            .SingleAsync(r => r.ReviewType == ProbationReviewType.FinalDecision && r.Id != review.Id);
        Assert.Equal(ProbationReviewStatus.Pending, newFinalReview.Status);
        Assert.Equal(newEndDate, newFinalReview.DueDate);
    }

    [Fact]
    public async Task ExecuteAsync_Completes_HrReview_Without_Outcome()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var (_, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.HrReview);

        var taskContext = BuildContext(companyId, Guid.NewGuid(), review.Id);

        await new CompleteProbationReviewFromTaskAction(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
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

        await new CompleteProbationReviewFromTaskAction(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
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

        await new CompleteProbationReviewFromTaskAction(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
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

        await new CompleteProbationReviewFromTaskAction(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .ExecuteAsync(taskContext, CancellationToken.None);

        var saved = await context.ProbationReviews.SingleAsync();
        Assert.NotEqual(completedBy, saved.CompletedByEmployeeId);
    }

    [Fact]
    public async Task ExecuteAsync_Publishes_ProbationPassed_IntegrationEvent_On_Pass_Outcome()
    {
        await using var context = BuildContext();
        var companyId   = Guid.NewGuid();
        var completedBy = Guid.NewGuid();

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);

        var taskContext = BuildContext(companyId, completedBy, review.Id, outcomeDecision: "Pass", notes: "Excellent.");
        var integrationPublisher = new Infrastructure.CapturingIntegrationEventPublisher();

        await new CompleteProbationReviewFromTaskAction(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), integrationPublisher, TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .ExecuteAsync(taskContext, CancellationToken.None);

        var evt = Assert.IsType<ProbationPassedIntegrationEvent>(Assert.Single(integrationPublisher.Published));
        Assert.Equal(companyId, evt.CompanyId);
        Assert.Equal(record.EmployeeId, evt.EmployeeId);
        Assert.Equal(record.Id, evt.ProbationRecordId);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Publish_ProbationPassed_IntegrationEvent_On_Fail_Outcome()
    {
        await using var context = BuildContext();
        var companyId   = Guid.NewGuid();
        var completedBy = Guid.NewGuid();

        var (_, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);

        var taskContext = BuildContext(companyId, completedBy, review.Id, outcomeDecision: "Fail", notes: "Did not meet targets.");
        var integrationPublisher = new Infrastructure.CapturingIntegrationEventPublisher();

        await new CompleteProbationReviewFromTaskAction(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), integrationPublisher, TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .ExecuteAsync(taskContext, CancellationToken.None);

        Assert.Empty(integrationPublisher.Published);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Publish_ProbationPassed_IntegrationEvent_On_Extend_Outcome()
    {
        await using var context = BuildContext();
        var companyId   = Guid.NewGuid();
        var completedBy = Guid.NewGuid();
        var newEndDate  = new DateOnly(2026, 10, 7);

        var (_, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);

        var taskContext = BuildContext(companyId, completedBy, review.Id,
            outcomeDecision: $"Extend|{newEndDate:yyyy-MM-dd}",
            notes: "Needs more time.");
        var integrationPublisher = new Infrastructure.CapturingIntegrationEventPublisher();

        await new CompleteProbationReviewFromTaskAction(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), integrationPublisher, TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .ExecuteAsync(taskContext, CancellationToken.None);

        Assert.Empty(integrationPublisher.Published);
    }

    [Fact]
    public async Task ExecuteAsync_Extend_Creates_ExtensionConfirmation_And_New_FinalDecision_Review_And_Tasks()
    {
        await using var context = BuildContext();
        var companyId   = Guid.NewGuid();
        var completedBy = Guid.NewGuid();
        var newEndDate  = new DateOnly(2026, 10, 7);

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);

        var taskContext = BuildContext(companyId, completedBy, review.Id,
            outcomeDecision: $"Extend|{newEndDate:yyyy-MM-dd}",
            notes: "Needs more time.");

        var taskCreator = new Infrastructure.FakeTaskCreator();
        var extensionService = TestProbationExtensionServiceFactory.Build(context, taskCreator: taskCreator);

        await new CompleteProbationReviewFromTaskAction(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), extensionService, new FakeNotificationWriter())
            .ExecuteAsync(taskContext, CancellationToken.None);

        var extensionConfirmation = await context.ProbationReviews
            .SingleAsync(r => r.ReviewType == ProbationReviewType.ExtensionConfirmation);
        Assert.Equal(ProbationReviewStatus.Pending, extensionConfirmation.Status);

        var newFinalReview = await context.ProbationReviews
            .SingleAsync(r => r.ReviewType == ProbationReviewType.FinalDecision && r.Id != review.Id);
        Assert.Equal(ProbationReviewStatus.Pending, newFinalReview.Status);
        Assert.Equal(newEndDate, newFinalReview.DueDate);

        Assert.Equal(2, taskCreator.Created.Count);
    }

    [Fact]
    public async Task ExecuteAsync_Repeated_Execution_Is_Idempotent_And_Does_Not_Duplicate_Extension_Reviews()
    {
        await using var context = BuildContext();
        var companyId   = Guid.NewGuid();
        var completedBy = Guid.NewGuid();
        var newEndDate  = new DateOnly(2026, 10, 7);

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);

        var taskContext = BuildContext(companyId, completedBy, review.Id,
            outcomeDecision: $"Extend|{newEndDate:yyyy-MM-dd}",
            notes: "Needs more time.");

        var action = new CompleteProbationReviewFromTaskAction(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter());

        await action.ExecuteAsync(taskContext, CancellationToken.None);
        var reviewCountAfterFirst = await context.ProbationReviews.CountAsync();

        // Retry the same completion request — the review is now Pending's terminal state
        // (Completed), so the guard `review.Status != Pending` no-ops the second call.
        var actionForRetry = new CompleteProbationReviewFromTaskAction(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter());
        await actionForRetry.ExecuteAsync(taskContext, CancellationToken.None);

        var reviewCountAfterSecond = await context.ProbationReviews.CountAsync();
        Assert.Equal(reviewCountAfterFirst, reviewCountAfterSecond);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Nothing_When_Review_Is_Cancelled()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var (_, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);
        review.Cancel(Guid.NewGuid(), Now);
        await context.SaveChangesAsync();

        var completedBy = Guid.NewGuid();
        var taskContext = BuildContext(companyId, completedBy, review.Id,
            outcomeDecision: "Extend|2026-10-07",
            notes: "Stale task.");

        await new CompleteProbationReviewFromTaskAction(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .ExecuteAsync(taskContext, CancellationToken.None);

        var saved = await context.ProbationReviews.SingleAsync(r => r.Id == review.Id);
        Assert.Equal(ProbationReviewStatus.Cancelled, saved.Status);
        Assert.NotEqual(completedBy, saved.CompletedByEmployeeId);
    }

    [Fact]
    public async Task ExecuteAsync_Pass_Outcome_Sends_ProbationOutcomeRecorded_Notification()
    {
        await using var context = BuildContext();
        var companyId   = Guid.NewGuid();
        var completedBy = Guid.NewGuid();

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);

        var taskContext = BuildContext(companyId, completedBy, review.Id, outcomeDecision: "Pass", notes: "Excellent.");
        var notificationWriter = new FakeNotificationWriter();

        await new CompleteProbationReviewFromTaskAction(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), notificationWriter)
            .ExecuteAsync(taskContext, CancellationToken.None);

        var notification = Assert.Single(notificationWriter.Written);
        Assert.Equal(record.EmployeeId, notification.EmployeeId);
        Assert.Equal(NotificationType.ProbationOutcomeRecorded, notification.Type);
    }

    [Fact]
    public async Task ExecuteAsync_Fail_Outcome_Sends_ProbationOutcomeRecorded_Notification()
    {
        await using var context = BuildContext();
        var companyId   = Guid.NewGuid();
        var completedBy = Guid.NewGuid();

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);

        var taskContext = BuildContext(companyId, completedBy, review.Id, outcomeDecision: "Fail", notes: "Did not meet targets.");
        var notificationWriter = new FakeNotificationWriter();

        await new CompleteProbationReviewFromTaskAction(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), notificationWriter)
            .ExecuteAsync(taskContext, CancellationToken.None);

        var notification = Assert.Single(notificationWriter.Written);
        Assert.Equal(record.EmployeeId, notification.EmployeeId);
        Assert.Equal(NotificationType.ProbationOutcomeRecorded, notification.Type);
    }

    [Fact]
    public async Task ExecuteAsync_Extend_Outcome_Does_Not_Send_ProbationOutcomeRecorded_Notification()
    {
        await using var context = BuildContext();
        var companyId   = Guid.NewGuid();
        var completedBy = Guid.NewGuid();
        var newEndDate  = new DateOnly(2026, 10, 7);

        var (_, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);

        var taskContext = BuildContext(companyId, completedBy, review.Id,
            outcomeDecision: $"Extend|{newEndDate:yyyy-MM-dd}",
            notes: "Needs more time.");
        var notificationWriter = new FakeNotificationWriter();

        await new CompleteProbationReviewFromTaskAction(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context, notificationWriter: notificationWriter), notificationWriter)
            .ExecuteAsync(taskContext, CancellationToken.None);

        Assert.DoesNotContain(
            notificationWriter.Written,
            n => n.Type == NotificationType.ProbationOutcomeRecorded);
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
            TaskSource.Probation,
            TaskActionType.Review,
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
