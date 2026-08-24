using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Services;
using HR.Modules.Probation.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Probation.Tests;

public class ProbationOutcomeNotifierTests
{
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly ExpectedEndDate = new(2026, 4, 1);
    private static readonly DateTimeOffset SeedNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NotifyNow = new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task NotifyAsync_Sends_Notification_To_Employee_On_Pass_Outcome()
    {
        var writer = new FakeNotificationWriter();
        var (record, review) = CreateCompletedRecordAndReview(ProbationOutcome.Pass);

        await ProbationOutcomeNotifier.NotifyAsync(writer, record, review, NotifyNow, CancellationToken.None);

        var notification = Assert.Single(writer.Written);
        Assert.Equal(record.EmployeeId, notification.EmployeeId);
        Assert.Equal(NotificationType.ProbationOutcomeRecorded, notification.Type);
    }

    [Fact]
    public async Task NotifyAsync_Sends_Notification_To_Employee_On_Fail_Outcome()
    {
        var writer = new FakeNotificationWriter();
        var (record, review) = CreateCompletedRecordAndReview(ProbationOutcome.Fail);

        await ProbationOutcomeNotifier.NotifyAsync(writer, record, review, NotifyNow, CancellationToken.None);

        var notification = Assert.Single(writer.Written);
        Assert.Equal(record.EmployeeId, notification.EmployeeId);
        Assert.Equal(NotificationType.ProbationOutcomeRecorded, notification.Type);
    }

    [Fact]
    public async Task NotifyAsync_Body_Never_Includes_Review_Notes()
    {
        var writer = new FakeNotificationWriter();
        const string sentinel = "SENSITIVE-REVIEW-NOTES-SENTINEL";
        var (record, review) = CreateCompletedRecordAndReview(ProbationOutcome.Pass, reviewNotes: sentinel);

        await ProbationOutcomeNotifier.NotifyAsync(writer, record, review, NotifyNow, CancellationToken.None);

        var notification = Assert.Single(writer.Written);
        Assert.DoesNotContain(sentinel, notification.Body);
    }

    [Fact]
    public async Task NotifyAsync_Body_Never_Includes_Record_OutcomeNotes()
    {
        var writer = new FakeNotificationWriter();
        const string sentinel = "SENSITIVE-OUTCOME-NOTES-SENTINEL";

        // Pass directly with the sentinel as outcome notes (rather than reusing
        // CreateCompletedRecordAndReview + a second Pass call) — PROB-05's transition guard now
        // rejects Passed->Passed, so the record must only transition once.
        var record = ProbationRecord.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            StartDate, ExpectedEndDate, null, SeedNow);
        record.Pass(Guid.NewGuid(), ExpectedEndDate, sentinel, SeedNow);

        var review = ProbationReview.Create(
            Guid.NewGuid(), record.CompanyId, record.Id, ProbationReviewType.FinalDecision, ExpectedEndDate, SeedNow);
        review.Complete(Guid.NewGuid(), ProbationOutcome.Pass, null, SeedNow);

        await ProbationOutcomeNotifier.NotifyAsync(writer, record, review, NotifyNow, CancellationToken.None);

        var notification = Assert.Single(writer.Written);
        Assert.DoesNotContain(sentinel, notification.Body);
    }

    [Fact]
    public async Task NotifyAsync_Body_Never_Includes_Record_ExtensionReason()
    {
        var writer = new FakeNotificationWriter();
        const string sentinel = "SENSITIVE-EXTENSION-REASON-SENTINEL";

        // Extend once (Active -> Extended is allowed) then Pass (Extended -> Passed is allowed) —
        // PROB-05's transition guard rejects Passed->Extended and Passed->Passed, so the record
        // must reach its terminal Pass state via a single valid transition chain.
        var record = ProbationRecord.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            StartDate, ExpectedEndDate, null, SeedNow);
        record.Extend(ExpectedEndDate.AddDays(30), sentinel, Guid.NewGuid(), ExpectedEndDate.AddDays(-1), SeedNow);
        record.Pass(Guid.NewGuid(), ExpectedEndDate.AddDays(30), null, SeedNow);

        var review = ProbationReview.Create(
            Guid.NewGuid(), record.CompanyId, record.Id, ProbationReviewType.FinalDecision, ExpectedEndDate, SeedNow);
        review.Complete(Guid.NewGuid(), ProbationOutcome.Pass, null, SeedNow);

        await ProbationOutcomeNotifier.NotifyAsync(writer, record, review, NotifyNow, CancellationToken.None);

        var notification = Assert.Single(writer.Written);
        Assert.DoesNotContain(sentinel, notification.Body);
    }

    [Fact]
    public async Task NotifyAsync_Is_Idempotent_When_Called_Twice()
    {
        var writer = new FakeNotificationWriter();
        var (record, review) = CreateCompletedRecordAndReview(ProbationOutcome.Pass);

        await ProbationOutcomeNotifier.NotifyAsync(writer, record, review, NotifyNow, CancellationToken.None);
        await ProbationOutcomeNotifier.NotifyAsync(writer, record, review, NotifyNow, CancellationToken.None);

        Assert.Single(writer.Written);
    }

    private static (ProbationRecord Record, ProbationReview Review) CreateCompletedRecordAndReview(
        ProbationOutcome outcome, string? reviewNotes = null)
    {
        var record = ProbationRecord.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            StartDate, ExpectedEndDate, null, SeedNow);

        if (outcome == ProbationOutcome.Pass)
            record.Pass(Guid.NewGuid(), ExpectedEndDate, null, SeedNow);
        else
            record.Fail(Guid.NewGuid(), ExpectedEndDate, null, SeedNow);

        var review = ProbationReview.Create(
            Guid.NewGuid(), record.CompanyId, record.Id, ProbationReviewType.FinalDecision, ExpectedEndDate, SeedNow);
        review.Complete(Guid.NewGuid(), outcome, reviewNotes, SeedNow);

        return (record, review);
    }
}
