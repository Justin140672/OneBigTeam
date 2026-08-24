using HR.Modules.Tasks.Contracts;
using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Services;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Probation.Jobs;

internal sealed class GenerateDueProbationReviewsJob(
    ProbationDbContext dbContext,
    IClock clock,
    ICompanyTimeZoneReader timeZoneReader,
    ICompanyProbationSettingsReader probationSettingsReader,
    ITaskCreator taskCreator,
    IEmployeeNameReader employeeNameReader,
    IHrAdministratorDirectory hrAdministratorDirectory,
    INotificationWriter notificationWriter,
    ILogger<GenerateDueProbationReviewsJob> logger)
{
    public async Task ExecuteAsync()
    {
        var now = clock.UtcNowOffset();

        var activeRecords = await dbContext.ProbationRecords
            .Where(r => r.Status == ProbationStatus.Active || r.Status == ProbationStatus.ReviewDue)
            .ToListAsync();

        if (activeRecords.Count == 0)
            return;

        // Records may belong to different companies each with their own configured time zone,
        // checkpoint schedule and HR administrator roster, so "today" (the review due-date
        // boundary), the checkpoint days and the HR queue must all be resolved per company rather
        // than once globally.
        var todayByCompany = new Dictionary<Guid, DateOnly>();
        var checkpointDaysByCompany = new Dictionary<Guid, IReadOnlyList<int>>();
        var hrAdministratorIdsByCompany = new Dictionary<Guid, IReadOnlyList<Guid>>();
        foreach (var companyId in activeRecords.Select(r => r.CompanyId).Distinct())
        {
            var timeZoneId = await timeZoneReader.GetTimeZoneAsync(companyId, CancellationToken.None);
            todayByCompany[companyId] = clock.TodayIn(timeZoneId);
            checkpointDaysByCompany[companyId] =
                await probationSettingsReader.GetCheckpointDaysAsync(companyId, CancellationToken.None);
            hrAdministratorIdsByCompany[companyId] =
                await hrAdministratorDirectory.GetHrAdministratorEmployeeIdsAsync(companyId, CancellationToken.None);
        }

        var recordIds = activeRecords.Select(r => r.Id).ToList();

        var existingReviewTypes = await dbContext.ProbationReviews
            .Where(r => recordIds.Contains(r.ProbationRecordId))
            .Select(r => new { r.ProbationRecordId, r.ReviewType })
            .ToListAsync();

        var existingByRecord = existingReviewTypes
            .GroupBy(r => r.ProbationRecordId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ReviewType).ToHashSet());

        var reviewsToCreate = new List<(ProbationReview Review, ProbationRecord Record)>();

        foreach (var record in activeRecords)
        {
            var today = todayByCompany[record.CompanyId];

            var existing = existingByRecord.TryGetValue(record.Id, out var types)
                ? types
                : new HashSet<ProbationReviewType>();

            var createdAny = false;

            var checkpointDays = checkpointDaysByCompany[record.CompanyId];

            foreach (var (reviewType, dueDate) in ProbationReviewScheduler.BuildSchedule(
                record.StartDate, record.ExpectedEndDate, checkpointDays))
            {
                if (existing.Contains(reviewType) || dueDate > today)
                    continue;

                var review = ProbationReview.Create(
                    Guid.NewGuid(), record.CompanyId, record.Id, reviewType, dueDate, now);
                reviewsToCreate.Add((review, record));
                createdAny = true;
            }

            if (createdAny && record.Status == ProbationStatus.Active)
                record.MarkReviewDue(now);
        }

        if (reviewsToCreate.Count == 0)
            return;

        dbContext.ProbationReviews.AddRange(reviewsToCreate.Select(x => x.Review));
        await dbContext.SaveChangesAsync();

        logger.LogInformation(
            "GenerateDueProbationReviewsJob created {ReviewCount} review(s) across {RecordCount} record(s)",
            reviewsToCreate.Count,
            reviewsToCreate.Select(x => x.Review.ProbationRecordId).Distinct().Count());

        foreach (var companyGroup in reviewsToCreate.GroupBy(x => x.Record.CompanyId))
        {
            var companyId = companyGroup.Key;
            var employeeIds = companyGroup.Select(x => x.Record.EmployeeId).Distinct();
            var names = await employeeNameReader.GetNamesAsync(companyId, employeeIds, CancellationToken.None);
            var hrAdministratorIds = hrAdministratorIdsByCompany[companyId];

            foreach (var (review, record) in companyGroup)
            {
                var employeeName = names.GetValueOrDefault(record.EmployeeId, "Unknown Employee");

                var assigneeId = ProbationReviewAssignment.ResolveTaskAssignee(
                    record, review.ReviewType, hrAdministratorIds);

                // notifyAssignee: false — the review-due notification below is more specific (and,
                // for HrReview, fans out to every HR administrator rather than just the single
                // deterministic task assignee), so we don't also want the generic "New task
                // assigned" notification duplicating it for the assignee.
                await taskCreator.CreateAsync(
                    record.CompanyId,
                    record.EmployeeId,
                    $"Complete probation review — {employeeName}",
                    $"Probation {ReviewTypeLabel(review.ReviewType)} due {review.DueDate:d MMM yyyy}.",
                    TaskPriority.High,
                    TaskSource.Probation,
                    TaskActionType.Review,
                    review.DueDate,
                    assignedEmployeeId: assigneeId,
                    assignedUserId: assigneeId,
                    sourceEntityId: review.Id,
                    CancellationToken.None,
                    notifyAssignee: false);

                await NotifyReviewDueAsync(record, review, employeeName, hrAdministratorIds, now, CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Notifies the audience confirmed for this review type that it is now due. Idempotent against
    /// duplicate job execution: guarded by <see cref="INotificationWriter.ExistsAsync"/> keyed on
    /// (recipient, review id, notification type), so a rerun over the same review — e.g. after a
    /// crash mid-batch — never sends a second copy. Notification text only ever includes the review
    /// type and due date; it never includes review notes or other free-text content.
    /// </summary>
    private async Task NotifyReviewDueAsync(
        ProbationRecord record,
        ProbationReview review,
        string employeeName,
        IReadOnlyList<Guid> hrAdministratorIds,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var recipients = ProbationReviewAssignment.ResolveNotificationRecipients(
            record, review.ReviewType, hrAdministratorIds);

        if (recipients.Count == 0)
        {
            logger.LogWarning(
                "Probation review {ProbationReviewId} ({ReviewType}) for record {ProbationRecordId} has no eligible notification recipients.",
                review.Id, review.ReviewType, record.Id);
            return;
        }

        var title = $"Probation {ReviewTypeLabel(review.ReviewType)} due — {employeeName}";
        var body = $"Probation {ReviewTypeLabel(review.ReviewType)} for {employeeName} is due {review.DueDate:d MMM yyyy}.";

        foreach (var recipientId in recipients)
        {
            var alreadySent = await notificationWriter.ExistsAsync(
                recipientId, review.Id, NotificationType.ProbationReviewDue, cancellationToken);

            if (alreadySent)
                continue;

            await notificationWriter.WriteAsync(
                Guid.NewGuid(),
                record.CompanyId,
                recipientId,
                title,
                body,
                review.Id,
                NotificationType.ProbationReviewDue,
                NotificationPriority.High,
                now,
                cancellationToken);
        }
    }

    private static string ReviewTypeLabel(ProbationReviewType reviewType) => reviewType switch
    {
        ProbationReviewType.ManagerCheckIn => "manager check-in",
        ProbationReviewType.HrReview       => "HR review",
        ProbationReviewType.FinalDecision  => "final decision",
        _                                  => reviewType.ToString()
    };
}
