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

        // Records may belong to different companies each with their own configured time zone and
        // checkpoint schedule, so "today" (the review due-date boundary) and the checkpoint days
        // must both be resolved per company rather than once globally.
        var todayByCompany = new Dictionary<Guid, DateOnly>();
        var checkpointDaysByCompany = new Dictionary<Guid, IReadOnlyList<int>>();
        foreach (var companyId in activeRecords.Select(r => r.CompanyId).Distinct())
        {
            var timeZoneId = await timeZoneReader.GetTimeZoneAsync(companyId, CancellationToken.None);
            todayByCompany[companyId] = clock.TodayIn(timeZoneId);
            checkpointDaysByCompany[companyId] =
                await probationSettingsReader.GetCheckpointDaysAsync(companyId, CancellationToken.None);
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

            foreach (var (review, record) in companyGroup)
            {
                var employeeName = names.GetValueOrDefault(record.EmployeeId, "Unknown Employee");

                await taskCreator.CreateAsync(
                    record.CompanyId,
                    record.EmployeeId,
                    $"Complete probation review — {employeeName}",
                    $"Probation {ReviewTypeLabel(review.ReviewType)} due {review.DueDate:d MMM yyyy}.",
                    TaskPriority.High,
                    TaskSource.Probation,
                    TaskActionType.Review,
                    review.DueDate,
                    assignedEmployeeId: record.ManagerEmployeeId,
                    assignedUserId: record.ManagerEmployeeId,
                    sourceEntityId: review.Id,
                    CancellationToken.None);
            }
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
