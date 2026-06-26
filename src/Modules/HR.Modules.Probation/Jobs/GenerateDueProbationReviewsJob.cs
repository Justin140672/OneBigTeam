using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Probation.Jobs;

internal sealed class GenerateDueProbationReviewsJob(
    ProbationDbContext dbContext,
    IClock clock,
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

        var reviewsToCreate = new List<ProbationReview>();

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

                reviewsToCreate.Add(ProbationReview.Create(
                    Guid.NewGuid(), record.CompanyId, record.Id, reviewType, dueDate, now));
                createdAny = true;
            }

            if (createdAny && record.Status == ProbationStatus.Active)
                record.MarkReviewDue(now);
        }

        if (reviewsToCreate.Count == 0)
            return;

        dbContext.ProbationReviews.AddRange(reviewsToCreate);
        await dbContext.SaveChangesAsync();

        logger.LogInformation(
            "GenerateDueProbationReviewsJob created {ReviewCount} review(s) across {RecordCount} record(s)",
            reviewsToCreate.Count,
            reviewsToCreate.Select(r => r.ProbationRecordId).Distinct().Count());
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
}
