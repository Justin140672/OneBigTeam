using HR.Infrastructure.Abstractions;
using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Tests.Infrastructure;
using HR.Modules.Tasks.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Tests;

public class ProbationExtensionServiceTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    [Fact]
    public async Task ApplyAsync_Creates_ExtensionConfirmation_And_New_FinalDecision_Reviews()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var decisionMakerId = Guid.NewGuid();
        var decisionDate = new DateOnly(2026, 9, 1);
        var newEndDate = new DateOnly(2026, 12, 1);
        var previousEndDate = new DateOnly(2026, 9, 15);

        var (record, sourceReview) = await SeedRecordAndReview(
            context, companyId, managerId, ProbationReviewType.FinalDecision, previousEndDate);

        var taskCreator = new FakeTaskCreator();
        var taskCanceller = new FakeTaskCanceller();
        var notificationWriter = new FakeNotificationWriter();
        var auditPublisher = new FakeAuditPublisher();

        var service = TestProbationExtensionServiceFactory.Build(
            context,
            taskCreator: taskCreator,
            taskCanceller: taskCanceller,
            notificationWriter: notificationWriter,
            auditPublisher: auditPublisher);

        await service.ApplyAsync(
            record, sourceReview, previousEndDate, newEndDate, "Needs more time.",
            decisionMakerId, decisionDate, Now, CancellationToken.None);

        var extensionConfirmation = await context.ProbationReviews
            .SingleAsync(r => r.ReviewType == ProbationReviewType.ExtensionConfirmation);
        Assert.Equal(ProbationReviewStatus.Pending, extensionConfirmation.Status);
        Assert.Equal(decisionDate, extensionConfirmation.DueDate);

        var newFinalReview = await context.ProbationReviews
            .SingleAsync(r => r.ReviewType == ProbationReviewType.FinalDecision && r.Id != sourceReview.Id);
        Assert.Equal(ProbationReviewStatus.Pending, newFinalReview.Status);
        Assert.Equal(newEndDate, newFinalReview.DueDate);

        Assert.Equal(2, taskCreator.Created.Count);
        Assert.Contains(taskCreator.Created, t => t.SourceEntityId == extensionConfirmation.Id && t.AssignedEmployeeId == managerId);
        Assert.Contains(taskCreator.Created, t => t.SourceEntityId == newFinalReview.Id && t.AssignedEmployeeId == managerId);

        Assert.Contains(taskCanceller.Calls, c => c.SourceEntityId == sourceReview.Id);

        Assert.Contains(notificationWriter.Written, n => n.EmployeeId == record.EmployeeId && n.Type == NotificationType.ProbationExtended);
        Assert.Contains(notificationWriter.Written, n => n.EmployeeId == managerId && n.Type == NotificationType.ProbationExtended);

        var evt = Assert.IsType<ProbationExtendedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal(companyId, evt.CompanyId);
        Assert.Equal(record.Id, evt.ProbationRecordId);
        Assert.Equal(record.EmployeeId, evt.EmployeeId);
        Assert.Equal(decisionMakerId, evt.DecisionMakerEmployeeId);
        Assert.Equal(previousEndDate, evt.PreviousExpectedEndDate);
        Assert.Equal(newEndDate, evt.NewExpectedEndDate);
        Assert.True(evt.HasExtensionReason);
        Assert.Equal(decisionDate, evt.DecisionDate);

        var serialized = System.Text.Json.JsonSerializer.Serialize(evt);
        Assert.DoesNotContain("Needs more time.", serialized);
    }

    [Fact]
    public async Task ApplyAsync_HasExtensionReason_False_When_Reason_Is_Whitespace()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var decisionMakerId = Guid.NewGuid();
        var previousEndDate = new DateOnly(2026, 9, 15);
        var newEndDate = new DateOnly(2026, 12, 1);

        var (record, sourceReview) = await SeedRecordAndReview(
            context, companyId, managerId, ProbationReviewType.FinalDecision, previousEndDate);

        var auditPublisher = new FakeAuditPublisher();
        var service = TestProbationExtensionServiceFactory.Build(context, auditPublisher: auditPublisher);

        await service.ApplyAsync(
            record, sourceReview, previousEndDate, newEndDate, "   ",
            decisionMakerId, new DateOnly(2026, 9, 1), Now, CancellationToken.None);

        var evt = Assert.IsType<ProbationExtendedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.False(evt.HasExtensionReason);
    }

    [Fact]
    public async Task ApplyAsync_Publishes_ProbationExtendedIntegrationEvent_With_NewExpectedEndDate()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var decisionMakerId = Guid.NewGuid();
        var previousEndDate = new DateOnly(2026, 9, 15);
        var newEndDate = new DateOnly(2026, 12, 1);

        var (record, sourceReview) = await SeedRecordAndReview(
            context, companyId, managerId, ProbationReviewType.FinalDecision, previousEndDate);

        var integrationPublisher = new CapturingIntegrationEventPublisher();
        var service = TestProbationExtensionServiceFactory.Build(context, integrationEventPublisher: integrationPublisher);

        await service.ApplyAsync(
            record, sourceReview, previousEndDate, newEndDate, "Needs more time.",
            decisionMakerId, new DateOnly(2026, 9, 1), Now, CancellationToken.None);

        var evt = Assert.IsType<HR.SharedKernel.ProbationExtendedIntegrationEvent>(
            Assert.Single(integrationPublisher.Published));
        Assert.Equal(companyId, evt.CompanyId);
        Assert.Equal(record.EmployeeId, evt.EmployeeId);
        Assert.Equal(record.Id, evt.ProbationRecordId);
        Assert.Equal(newEndDate, evt.NewExpectedEndDate);
    }

    [Fact]
    public async Task ApplyAsync_Supersedes_Other_Pending_FinalDecision_Reviews()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var decisionMakerId = Guid.NewGuid();
        var previousEndDate = new DateOnly(2026, 9, 15);
        var newEndDate = new DateOnly(2026, 12, 1);

        // sourceReview is the one being completed with Extend (e.g. an earlier HrReview).
        var (record, sourceReview) = await SeedRecordAndReview(
            context, companyId, managerId, ProbationReviewType.HrReview, previousEndDate);

        // Simulate the daily scheduling job already having created a Pending FinalDecision review
        // for the pre-extension expected end date.
        var otherFinalReview = ProbationReview.Create(
            Guid.NewGuid(), companyId, record.Id, ProbationReviewType.FinalDecision, previousEndDate, Now);
        context.ProbationReviews.Add(otherFinalReview);
        await context.SaveChangesAsync();

        var taskCanceller = new FakeTaskCanceller();
        var service = TestProbationExtensionServiceFactory.Build(context, taskCanceller: taskCanceller);

        await service.ApplyAsync(
            record, sourceReview, previousEndDate, newEndDate, "Needs more time.",
            decisionMakerId, new DateOnly(2026, 9, 1), Now, CancellationToken.None);

        var reloaded = await context.ProbationReviews.SingleAsync(r => r.Id == otherFinalReview.Id);
        Assert.Equal(ProbationReviewStatus.Cancelled, reloaded.Status);
        Assert.Equal(sourceReview.Id, reloaded.SupersededByReviewId);

        Assert.Contains(taskCanceller.Calls, c => c.SourceEntityId == otherFinalReview.Id);
    }

    [Fact]
    public async Task ApplyAsync_Does_Not_Notify_Manager_Twice_When_Manager_Is_DecisionMaker()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var previousEndDate = new DateOnly(2026, 9, 15);
        var newEndDate = new DateOnly(2026, 12, 1);

        var (record, sourceReview) = await SeedRecordAndReview(
            context, companyId, managerId, ProbationReviewType.FinalDecision, previousEndDate);

        var notificationWriter = new FakeNotificationWriter();
        var service = TestProbationExtensionServiceFactory.Build(context, notificationWriter: notificationWriter);

        // Manager is also the decision maker.
        await service.ApplyAsync(
            record, sourceReview, previousEndDate, newEndDate, "Needs more time.",
            managerId, new DateOnly(2026, 9, 1), Now, CancellationToken.None);

        var managerNotifications = notificationWriter.Written
            .Where(n => n.EmployeeId == managerId && n.Type == NotificationType.ProbationExtended)
            .ToList();

        // Manager still receives an employee-role notification if they are also the employee
        // being reviewed, but here manager != employee so they should receive exactly zero
        // manager-specific notifications (the dedup guard skips them as decision maker).
        Assert.Empty(managerNotifications);
    }

    [Fact]
    public async Task ApplyAsync_Notifies_Seeded_Hr_Administrators_Once_Each()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var decisionMakerId = Guid.NewGuid();
        var hrAdmin1 = Guid.NewGuid();
        var hrAdmin2 = Guid.NewGuid();
        var previousEndDate = new DateOnly(2026, 9, 15);
        var newEndDate = new DateOnly(2026, 12, 1);

        var (record, sourceReview) = await SeedRecordAndReview(
            context, companyId, managerId, ProbationReviewType.FinalDecision, previousEndDate);

        var hrAdministratorDirectory = new FakeHrAdministratorDirectory();
        hrAdministratorDirectory.Seed(companyId, hrAdmin1, hrAdmin2);

        var notificationWriter = new FakeNotificationWriter();
        var service = TestProbationExtensionServiceFactory.Build(
            context, notificationWriter: notificationWriter, hrAdministratorDirectory: hrAdministratorDirectory);

        await service.ApplyAsync(
            record, sourceReview, previousEndDate, newEndDate, "Needs more time.",
            decisionMakerId, new DateOnly(2026, 9, 1), Now, CancellationToken.None);

        Assert.Single(notificationWriter.Written, n => n.EmployeeId == hrAdmin1);
        Assert.Single(notificationWriter.Written, n => n.EmployeeId == hrAdmin2);
    }

    [Fact]
    public async Task ApplyAsync_Deduplicates_When_Hr_Administrator_Is_Also_The_Manager()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var decisionMakerId = Guid.NewGuid();
        var previousEndDate = new DateOnly(2026, 9, 15);
        var newEndDate = new DateOnly(2026, 12, 1);

        var (record, sourceReview) = await SeedRecordAndReview(
            context, companyId, managerId, ProbationReviewType.FinalDecision, previousEndDate);

        var hrAdministratorDirectory = new FakeHrAdministratorDirectory();
        hrAdministratorDirectory.Seed(companyId, managerId);

        var notificationWriter = new FakeNotificationWriter();
        var service = TestProbationExtensionServiceFactory.Build(
            context, notificationWriter: notificationWriter, hrAdministratorDirectory: hrAdministratorDirectory);

        await service.ApplyAsync(
            record, sourceReview, previousEndDate, newEndDate, "Needs more time.",
            decisionMakerId, new DateOnly(2026, 9, 1), Now, CancellationToken.None);

        var managerNotifications = notificationWriter.Written
            .Count(n => n.EmployeeId == managerId && n.Type == NotificationType.ProbationExtended);

        Assert.Equal(1, managerNotifications);
    }

    [Fact]
    public async Task ApplyAsync_Called_Twice_In_Sequence_Produces_Two_Distinct_Extension_Cycles()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var decisionMakerId = Guid.NewGuid();
        var firstPreviousEndDate = new DateOnly(2026, 9, 15);
        var firstNewEndDate = new DateOnly(2026, 12, 1);

        var (record, firstSourceReview) = await SeedRecordAndReview(
            context, companyId, managerId, ProbationReviewType.FinalDecision, firstPreviousEndDate);

        var service = TestProbationExtensionServiceFactory.Build(context);

        // First extension cycle.
        await service.ApplyAsync(
            record, firstSourceReview, firstPreviousEndDate, firstNewEndDate, "First extension.",
            decisionMakerId, new DateOnly(2026, 9, 1), Now, CancellationToken.None);

        var firstFinalReview = await context.ProbationReviews
            .SingleAsync(r => r.ReviewType == ProbationReviewType.FinalDecision
                && r.Id != firstSourceReview.Id
                && r.Status == ProbationReviewStatus.Pending);

        // Second extension: the newly created FinalDecision review is completed (with Extend)
        // and becomes the sourceReview for the second cycle. The handler would normally call
        // review.Complete(...) before invoking ApplyAsync; mirror that here.
        firstFinalReview.Complete(decisionMakerId, ProbationOutcome.Extend, "Needs even more time.", Now);
        await context.SaveChangesAsync();

        var secondPreviousEndDate = firstNewEndDate;
        var secondNewEndDate = new DateOnly(2027, 2, 1);

        await service.ApplyAsync(
            record, firstFinalReview, secondPreviousEndDate, secondNewEndDate, "Second extension.",
            decisionMakerId, new DateOnly(2026, 12, 1), Now, CancellationToken.None);

        var extensionConfirmations = await context.ProbationReviews
            .Where(r => r.ReviewType == ProbationReviewType.ExtensionConfirmation)
            .ToListAsync();
        Assert.Equal(2, extensionConfirmations.Count);

        var pendingFinalReviews = await context.ProbationReviews
            .Where(r => r.ReviewType == ProbationReviewType.FinalDecision && r.Status == ProbationReviewStatus.Pending)
            .ToListAsync();
        Assert.Single(pendingFinalReviews);
        Assert.Equal(secondNewEndDate, pendingFinalReviews[0].DueDate);

        // The first FinalDecision review created by the first extension is Completed (driven
        // explicitly above), not Cancelled — ApplyAsync only cancels *other* still-Pending
        // FinalDecision reviews, and this one was already completed before the second call.
        var reloadedFirstFinal = await context.ProbationReviews.SingleAsync(r => r.Id == firstFinalReview.Id);
        Assert.Equal(ProbationReviewStatus.Completed, reloadedFirstFinal.Status);
    }

    private static async Task<(ProbationRecord record, ProbationReview review)> SeedRecordAndReview(
        ProbationDbContext context,
        Guid companyId,
        Guid managerId,
        ProbationReviewType reviewType,
        DateOnly dueDate)
    {
        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), managerId,
            new DateOnly(2026, 6, 1), dueDate, null, DateOnly.FromDateTime(Now.UtcDateTime), Now);
        context.ProbationRecords.Add(record);

        var review = ProbationReview.Create(
            Guid.NewGuid(), companyId, record.Id, reviewType, dueDate, Now);
        context.ProbationReviews.Add(review);

        await context.SaveChangesAsync();
        return (record, review);
    }

    private static ProbationDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ProbationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
