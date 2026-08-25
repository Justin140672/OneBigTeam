using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Features.CompleteProbationReview;
using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Tests.Infrastructure;
using HR.SharedKernel;
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

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                Notes = "All targets met."
            }, completedBy, CancellationToken.None);

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

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
            }, Guid.NewGuid(), CancellationToken.None);

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

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                Notes = "Excellent performance.",
                Outcome = ProbationOutcome.Pass,
                DecisionDate = decisionDate
            }, completedBy, CancellationToken.None);

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

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                Notes = "Did not meet targets.",
                Outcome = ProbationOutcome.Fail,
                DecisionDate = decisionDate
            }, completedBy, CancellationToken.None);

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

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                Notes = "Needs more time.",
                Outcome = ProbationOutcome.Extend,
                DecisionDate = decisionDate,
                NewExpectedEndDate = newEndDate,
                ExtensionReason = "Did not meet all targets yet."
            }, completedBy, CancellationToken.None);

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

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
            }, Guid.NewGuid(), CancellationToken.None);

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

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                Outcome = ProbationOutcome.Extend,
                DecisionDate = new DateOnly(2026, 9, 1),
                NewExpectedEndDate = newEndDate,
                ExtensionReason = "Needs more time."
            }, completedBy, CancellationToken.None);

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

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
            }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_ValidationError_When_ExtensionConfirmation_Has_Pass_Outcome()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.ExtensionConfirmation);

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                Outcome = ProbationOutcome.Pass,
                DecisionDate = new DateOnly(2026, 9, 1)
            }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_ValidationError_When_Outcome_Set_On_ManagerCheckIn()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.ManagerCheckIn);

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                Outcome = ProbationOutcome.Pass,
                DecisionDate = new DateOnly(2026, 9, 1)
            }, Guid.NewGuid(), CancellationToken.None);

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

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
            }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_ProbationRecord_Does_Not_Exist()
    {
        await using var context = BuildContext();

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = Guid.NewGuid(),
                ProbationRecordId = Guid.NewGuid(),
                ReviewId = Guid.NewGuid(),
            }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Record_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();

        var record = ProbationRecord.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, DateOnly.FromDateTime(Now.UtcDateTime), Now);
        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = Guid.NewGuid(),
                ProbationRecordId = record.Id,
                ReviewId = Guid.NewGuid(),
            }, Guid.NewGuid(), CancellationToken.None);

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
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, DateOnly.FromDateTime(Now.UtcDateTime), Now);
        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = Guid.NewGuid(),
            }, Guid.NewGuid(), CancellationToken.None);

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
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, DateOnly.FromDateTime(Now.UtcDateTime), Now);
        var record2 = ProbationRecord.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, DateOnly.FromDateTime(Now.UtcDateTime), Now);
        context.ProbationRecords.AddRange(record1, record2);

        var review = ProbationReview.Create(
            Guid.NewGuid(), companyId, record2.Id, ProbationReviewType.ManagerCheckIn,
            new DateOnly(2026, 7, 1), Now);
        context.ProbationReviews.Add(review);
        await context.SaveChangesAsync();

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record1.Id,
                ReviewId = review.Id,
            }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Publishes_Audit_Event_With_EmployeeId_From_Record()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.ManagerCheckIn);

        var publisher = new FakeAuditPublisher();
        await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), publisher, new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
            }, Guid.NewGuid(), CancellationToken.None);

        var published = (IAuditEvent)Assert.Single(publisher.Published);
        Assert.Equal(record.EmployeeId, published.EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Publishes_ProbationPassed_IntegrationEvent_On_Pass_Outcome()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var completedBy = Guid.NewGuid();

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);

        var integrationPublisher = new Infrastructure.CapturingIntegrationEventPublisher();
        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), integrationPublisher, TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                Outcome = ProbationOutcome.Pass,
                DecisionDate = new DateOnly(2026, 9, 1)
            }, completedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var evt = Assert.IsType<ProbationPassedIntegrationEvent>(Assert.Single(integrationPublisher.Published));
        Assert.Equal(companyId, evt.CompanyId);
        Assert.Equal(record.EmployeeId, evt.EmployeeId);
        Assert.Equal(record.Id, evt.ProbationRecordId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_ProbationPassed_IntegrationEvent_On_Fail_Outcome()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var completedBy = Guid.NewGuid();

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);

        var integrationPublisher = new Infrastructure.CapturingIntegrationEventPublisher();
        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), integrationPublisher, TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                Outcome = ProbationOutcome.Fail,
                DecisionDate = new DateOnly(2026, 9, 1)
            }, completedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // PROB-07: a Fail outcome now publishes ProbationFailedIntegrationEvent (to drive the
        // ProbationFailed employee timeline entry) — it must never publish ProbationPassedIntegrationEvent.
        Assert.DoesNotContain(integrationPublisher.Published, e => e is ProbationPassedIntegrationEvent);
        var evt = Assert.IsType<ProbationFailedIntegrationEvent>(Assert.Single(integrationPublisher.Published));
        Assert.Equal(companyId, evt.CompanyId);
        Assert.Equal(record.EmployeeId, evt.EmployeeId);
        Assert.Equal(record.Id, evt.ProbationRecordId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_ProbationPassed_IntegrationEvent_On_Extend_Outcome()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var completedBy = Guid.NewGuid();

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.ExtensionConfirmation);

        var integrationPublisher = new Infrastructure.CapturingIntegrationEventPublisher();
        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), integrationPublisher, TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                Outcome = ProbationOutcome.Extend,
                DecisionDate = new DateOnly(2026, 9, 1),
                NewExpectedEndDate = new DateOnly(2026, 12, 1),
                ExtensionReason = "Needs more time."
            }, completedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(integrationPublisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Extend_Creates_ExtensionConfirmation_And_New_FinalDecision_Review_And_Tasks()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var completedBy = Guid.NewGuid();
        var newEndDate = new DateOnly(2026, 12, 1);

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);

        var taskCreator = new Infrastructure.FakeTaskCreator();
        var extensionService = TestProbationExtensionServiceFactory.Build(context, taskCreator: taskCreator);

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), extensionService, new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                Outcome = ProbationOutcome.Extend,
                DecisionDate = new DateOnly(2026, 9, 1),
                NewExpectedEndDate = newEndDate,
                ExtensionReason = "Needs more time."
            }, completedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);

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
    public async Task HandleAsync_Repeated_Execution_Fails_And_Does_Not_Duplicate_Extension_Reviews()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var completedBy = Guid.NewGuid();
        var newEndDate = new DateOnly(2026, 12, 1);

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);

        var request = new CompleteProbationReviewRequest
        {
            CompanyId = companyId,
            ProbationRecordId = record.Id,
            ReviewId = review.Id,
            Outcome = ProbationOutcome.Extend,
            DecisionDate = new DateOnly(2026, 9, 1),
            NewExpectedEndDate = newEndDate,
            ExtensionReason = "Needs more time."
        };

        var firstResult = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(request, completedBy, CancellationToken.None);
        Assert.True(firstResult.IsSuccess);

        var reviewCountAfterFirst = await context.ProbationReviews.CountAsync();

        var secondResult = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(request, completedBy, CancellationToken.None);

        Assert.True(secondResult.IsFailure);
        Assert.Equal("validation", secondResult.Error.Code);

        var reviewCountAfterSecond = await context.ProbationReviews.CountAsync();
        Assert.Equal(reviewCountAfterFirst, reviewCountAfterSecond);
    }

    [Fact]
    public async Task HandleAsync_Returns_ValidationError_When_Review_Is_Cancelled()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);
        review.Cancel(Guid.NewGuid(), Now);
        await context.SaveChangesAsync();

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                Outcome = ProbationOutcome.Pass,
                DecisionDate = new DateOnly(2026, 9, 1)
            }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("superseded", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_Second_Extension_Via_Newly_Created_FinalDecision_Review_Reflects_Latest_Decision()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var firstCompletedBy = Guid.NewGuid();
        var secondCompletedBy = Guid.NewGuid();
        var firstNewEndDate = new DateOnly(2026, 12, 1);
        var secondNewEndDate = new DateOnly(2027, 3, 1);

        var (record, reviewA) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);

        var firstResult = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = reviewA.Id,
                Outcome = ProbationOutcome.Extend,
                DecisionDate = new DateOnly(2026, 9, 1),
                NewExpectedEndDate = firstNewEndDate,
                ExtensionReason = "First extension."
            }, firstCompletedBy, CancellationToken.None);
        Assert.True(firstResult.IsSuccess);

        var reviewB = await context.ProbationReviews
            .SingleAsync(r => r.ReviewType == ProbationReviewType.FinalDecision && r.Id != reviewA.Id);

        var secondResult = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = reviewB.Id,
                Outcome = ProbationOutcome.Extend,
                DecisionDate = new DateOnly(2026, 12, 1),
                NewExpectedEndDate = secondNewEndDate,
                ExtensionReason = "Second extension."
            }, secondCompletedBy, CancellationToken.None);
        Assert.True(secondResult.IsSuccess);

        var extensionConfirmations = await context.ProbationReviews
            .Where(r => r.ReviewType == ProbationReviewType.ExtensionConfirmation)
            .CountAsync();
        Assert.Equal(2, extensionConfirmations);

        var persistedRecord = await context.ProbationRecords.SingleAsync();
        Assert.Equal(ProbationStatus.Extended, persistedRecord.Status);
        Assert.Equal(secondNewEndDate, persistedRecord.ExpectedEndDate);
        Assert.Equal("Second extension.", persistedRecord.ExtensionReason);
        Assert.Equal(secondCompletedBy, persistedRecord.DecisionMakerEmployeeId);
        Assert.Equal(new DateOnly(2026, 12, 1), persistedRecord.DecisionDate);
    }

    [Fact]
    public async Task HandleAsync_Extend_Supersedes_PreExisting_Pending_FinalDecision_Review()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var completedBy = Guid.NewGuid();
        var newEndDate = new DateOnly(2026, 12, 1);

        var (record, reviewBeingCompleted) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);

        // Simulate the daily scheduling job already having created a second, distinct Pending
        // FinalDecision review/task for the original expected end date (e.g. a re-scheduled
        // review generated before the manager completed reviewBeingCompleted).
        var preExistingFinalReview = ProbationReview.Create(
            Guid.NewGuid(), companyId, record.Id, ProbationReviewType.FinalDecision,
            record.ExpectedEndDate, Now);
        context.ProbationReviews.Add(preExistingFinalReview);
        await context.SaveChangesAsync();

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = reviewBeingCompleted.Id,
                Outcome = ProbationOutcome.Extend,
                DecisionDate = new DateOnly(2026, 9, 1),
                NewExpectedEndDate = newEndDate,
                ExtensionReason = "Needs more time."
            }, completedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var reloaded = await context.ProbationReviews.SingleAsync(r => r.Id == preExistingFinalReview.Id);
        Assert.Equal(ProbationReviewStatus.Cancelled, reloaded.Status);
    }

    [Fact]
    public async Task HandleAsync_Pass_Outcome_Sends_ProbationOutcomeRecorded_Notification()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);

        var notificationWriter = new FakeNotificationWriter();
        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), notificationWriter)
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                Outcome = ProbationOutcome.Pass,
                DecisionDate = new DateOnly(2026, 9, 1)
            }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var notification = Assert.Single(notificationWriter.Written);
        Assert.Equal(record.EmployeeId, notification.EmployeeId);
        Assert.Equal(HR.Infrastructure.Abstractions.NotificationType.ProbationOutcomeRecorded, notification.Type);
    }

    [Fact]
    public async Task HandleAsync_Fail_Outcome_Sends_ProbationOutcomeRecorded_Notification()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);

        var notificationWriter = new FakeNotificationWriter();
        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), notificationWriter)
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                Outcome = ProbationOutcome.Fail,
                DecisionDate = new DateOnly(2026, 9, 1)
            }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var notification = Assert.Single(notificationWriter.Written);
        Assert.Equal(record.EmployeeId, notification.EmployeeId);
        Assert.Equal(HR.Infrastructure.Abstractions.NotificationType.ProbationOutcomeRecorded, notification.Type);
    }

    [Fact]
    public async Task HandleAsync_Extend_Outcome_Does_Not_Send_ProbationOutcomeRecorded_Notification()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);

        var notificationWriter = new FakeNotificationWriter();
        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context, notificationWriter: notificationWriter), notificationWriter)
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                Outcome = ProbationOutcome.Extend,
                DecisionDate = new DateOnly(2026, 9, 1),
                NewExpectedEndDate = new DateOnly(2026, 12, 1),
                ExtensionReason = "Needs more time."
            }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(
            notificationWriter.Written,
            n => n.Type == HR.Infrastructure.Abstractions.NotificationType.ProbationOutcomeRecorded);
    }

    [Fact]
    public async Task HandleAsync_Extend_With_NewExpectedEndDate_Equal_To_Current_ExpectedEndDate_Fails_Without_Mutation()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                Outcome = ProbationOutcome.Extend,
                DecisionDate = new DateOnly(2026, 7, 15),
                NewExpectedEndDate = record.ExpectedEndDate, // equal to current ExpectedEndDate — not strictly forward
                ExtensionReason = "Needs more time."
            }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("current expected end date", result.Error.Message, StringComparison.OrdinalIgnoreCase);

        var persistedRecord = await context.ProbationRecords.SingleAsync();
        Assert.Equal(ProbationStatus.Active, persistedRecord.Status);
        Assert.Equal(record.ExpectedEndDate, persistedRecord.ExpectedEndDate);

        var persistedReview = await context.ProbationReviews.SingleAsync(r => r.Id == review.Id);
        Assert.Equal(ProbationReviewStatus.Pending, persistedReview.Status);
    }

    [Fact]
    public async Task HandleAsync_Extend_With_NewExpectedEndDate_After_Current_ExpectedEndDate_Succeeds()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                Outcome = ProbationOutcome.Extend,
                DecisionDate = new DateOnly(2026, 7, 15),
                NewExpectedEndDate = record.ExpectedEndDate.AddDays(1),
                ExtensionReason = "Needs more time."
            }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Extend_With_NewExpectedEndDate_Equal_To_DecisionDate_Fails_Without_Mutation()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);
        var decisionDate = record.ExpectedEndDate.AddMonths(1);

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                Outcome = ProbationOutcome.Extend,
                DecisionDate = decisionDate,
                NewExpectedEndDate = decisionDate, // equal to DecisionDate, and later than current ExpectedEndDate
                ExtensionReason = "Needs more time."
            }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("decision date", result.Error.Message, StringComparison.OrdinalIgnoreCase);

        var persistedRecord = await context.ProbationRecords.SingleAsync();
        Assert.Equal(ProbationStatus.Active, persistedRecord.Status);

        var persistedReview = await context.ProbationReviews.SingleAsync(r => r.Id == review.Id);
        Assert.Equal(ProbationReviewStatus.Pending, persistedReview.Status);
    }

    [Fact]
    public async Task HandleAsync_CompletedByEmployeeId_Parameter_Drives_DecisionMakerEmployeeId_Not_Request_Body()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var completedByEmployeeId = Guid.NewGuid();

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);

        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                Outcome = ProbationOutcome.Pass,
                DecisionDate = new DateOnly(2026, 9, 1)
            }, completedByEmployeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(completedByEmployeeId, result.Value!.CompletedByEmployeeId);

        var persistedRecord = await context.ProbationRecords.SingleAsync();
        Assert.Equal(completedByEmployeeId, persistedRecord.DecisionMakerEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Pass_Outcome_Publishes_ProbationPassedAuditEvent_With_Correct_Fields()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var completedBy = Guid.NewGuid();
        var decisionDate = new DateOnly(2026, 9, 1);

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);

        var publisher = new FakeAuditPublisher();
        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), publisher, new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                Notes = "Excellent performance.",
                Outcome = ProbationOutcome.Pass,
                DecisionDate = decisionDate
            }, completedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var evt = Assert.IsType<ProbationPassedAuditEvent>(Assert.Single(publisher.Published));
        Assert.Equal(companyId, evt.CompanyId);
        Assert.Equal(record.Id, evt.ProbationRecordId);
        Assert.Equal(review.Id, evt.ProbationReviewId);
        Assert.Equal(record.EmployeeId, evt.EmployeeId);
        Assert.Equal(completedBy, evt.DecisionMakerEmployeeId);
        Assert.Equal(decisionDate, evt.DecisionDate);
        Assert.True(evt.HasNotes);

        var serialized = System.Text.Json.JsonSerializer.Serialize(evt);
        Assert.DoesNotContain("Excellent performance.", serialized);
    }

    [Fact]
    public async Task HandleAsync_Fail_Outcome_Publishes_ProbationFailedAuditEvent_And_IntegrationEvent()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var completedBy = Guid.NewGuid();
        var decisionDate = new DateOnly(2026, 9, 1);

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.FinalDecision);

        var auditPublisher = new FakeAuditPublisher();
        var integrationPublisher = new Infrastructure.CapturingIntegrationEventPublisher();
        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), auditPublisher, integrationPublisher, TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                Notes = "Did not meet targets.",
                Outcome = ProbationOutcome.Fail,
                DecisionDate = decisionDate
            }, completedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var evt = Assert.IsType<ProbationFailedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal(companyId, evt.CompanyId);
        Assert.Equal(record.Id, evt.ProbationRecordId);
        Assert.Equal(review.Id, evt.ProbationReviewId);
        Assert.Equal(record.EmployeeId, evt.EmployeeId);
        Assert.Equal(completedBy, evt.DecisionMakerEmployeeId);
        Assert.Equal(decisionDate, evt.DecisionDate);
        Assert.True(evt.HasNotes);

        var serialized = System.Text.Json.JsonSerializer.Serialize(evt);
        Assert.DoesNotContain("Did not meet targets.", serialized);

        var integrationEvt = Assert.IsType<HR.SharedKernel.ProbationFailedIntegrationEvent>(
            Assert.Single(integrationPublisher.Published));
        Assert.Equal(companyId, integrationEvt.CompanyId);
        Assert.Equal(record.EmployeeId, integrationEvt.EmployeeId);
        Assert.Equal(record.Id, integrationEvt.ProbationRecordId);
    }

    [Fact]
    public async Task HandleAsync_Checkpoint_Review_Publishes_ProbationReviewCompletedAuditEvent_Only()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var completedBy = Guid.NewGuid();

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.ManagerCheckIn);

        var publisher = new FakeAuditPublisher();
        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), publisher, new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                Notes = "All targets met."
            }, completedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var evt = Assert.IsType<ProbationReviewCompletedAuditEvent>(Assert.Single(publisher.Published));
        Assert.Equal(companyId, evt.CompanyId);
        Assert.Equal(review.Id, evt.ProbationReviewId);
        Assert.Equal(record.Id, evt.ProbationRecordId);
        Assert.Equal(record.EmployeeId, evt.EmployeeId);
        Assert.Equal(completedBy, evt.CompletedByEmployeeId);
        Assert.True(evt.HasNotes);

        var serialized = System.Text.Json.JsonSerializer.Serialize(evt);
        Assert.DoesNotContain("All targets met.", serialized);
    }

    [Fact]
    public async Task HandleAsync_Extend_Outcome_Does_Not_Publish_ProbationReviewCompletedAuditEvent()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var completedBy = Guid.NewGuid();

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.ExtensionConfirmation);

        var publisher = new FakeAuditPublisher();
        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), publisher, new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
                Outcome = ProbationOutcome.Extend,
                DecisionDate = new DateOnly(2026, 9, 1),
                NewExpectedEndDate = new DateOnly(2026, 12, 1),
                ExtensionReason = "Needs more time."
            }, completedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(publisher.Published, e => e is ProbationReviewCompletedAuditEvent);
        Assert.DoesNotContain(publisher.Published, e => e is ProbationPassedAuditEvent);
        Assert.DoesNotContain(publisher.Published, e => e is ProbationFailedAuditEvent);
    }

    [Fact]
    public async Task HandleAsync_Checkpoint_Review_HasNotes_False_When_No_Notes_Provided()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var (record, review) = await SeedRecordAndReview(context, companyId, ProbationReviewType.HrReview);

        var publisher = new FakeAuditPublisher();
        var result = await new CompleteProbationReviewHandler(context, new FakeClock(FixedUtcNow), publisher, new NoOpIntegrationEventPublisher(), TestProbationExtensionServiceFactory.Build(context), new FakeNotificationWriter())
            .HandleAsync(new CompleteProbationReviewRequest
            {
                CompanyId = companyId,
                ProbationRecordId = record.Id,
                ReviewId = review.Id,
            }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var evt = Assert.IsType<ProbationReviewCompletedAuditEvent>(Assert.Single(publisher.Published));
        Assert.False(evt.HasNotes);
    }

    private static async Task<(ProbationRecord record, ProbationReview review)> SeedRecordAndReview(
        ProbationDbContext context,
        Guid companyId,
        ProbationReviewType reviewType)
    {
        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, DateOnly.FromDateTime(Now.UtcDateTime), Now);
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
