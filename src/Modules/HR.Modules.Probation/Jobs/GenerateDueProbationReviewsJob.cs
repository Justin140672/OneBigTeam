using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Probation.Jobs;

internal sealed class GenerateDueProbationReviewsJob(
    ProbationDbContext dbContext,
    IClock clock,
    ITaskCreator taskCreator,
    IEmployeeNameReader employeeNameReader,
    ILogger<GenerateDueProbationReviewsJob> logger)
{
    public async Task ExecuteAsync()
    {
        var today = DateOnly.FromDateTime(clock.UtcNow);
        var now = clock.UtcNowOffset();

        var activeRecords = await dbContext.ProbationRecords
            .Where(r => r.Status == ProbationStatus.Active || r.Status == ProbationStatus.ReviewDue)
            .ToListAsync();

        if (activeRecords.Count == 0)
            return;

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
            var existing = existingByRecord.TryGetValue(record.Id, out var types)
                ? types
                : new HashSet<ProbationReviewType>();

            var createdAny = false;

            foreach (var (reviewType, dueDate) in ComputeSchedule(record))
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
                    TaskSource.ProbationReview,
                    review.DueDate,
                    assignedEmployeeId: record.ManagerEmployeeId,
                    assignedUserId: record.ManagerEmployeeId,
                    sourceEntityId: review.Id,
                    CancellationToken.None);
            }
        }
    }

    // Reviews are scheduled proportionally across the probation period:
    // ManagerCheckIn at 1/3, HrReview at 2/3, FinalDecision at the end.
    private static IEnumerable<(ProbationReviewType ReviewType, DateOnly DueDate)> ComputeSchedule(
        ProbationRecord record)
    {
        var totalDays = record.ExpectedEndDate.DayNumber - record.StartDate.DayNumber;

        yield return (ProbationReviewType.ManagerCheckIn, record.StartDate.AddDays(totalDays / 3));
        yield return (ProbationReviewType.HrReview, record.StartDate.AddDays(2 * totalDays / 3));
        yield return (ProbationReviewType.FinalDecision, record.ExpectedEndDate);
    }

    private static string ReviewTypeLabel(ProbationReviewType reviewType) => reviewType switch
    {
        ProbationReviewType.ManagerCheckIn => "manager check-in",
        ProbationReviewType.HrReview       => "HR review",
        ProbationReviewType.FinalDecision  => "final decision",
        _                                  => reviewType.ToString()
    };
}
