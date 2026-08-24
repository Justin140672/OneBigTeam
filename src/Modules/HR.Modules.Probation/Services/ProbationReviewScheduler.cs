using HR.Modules.Probation.Domain;

namespace HR.Modules.Probation.Services;

/// <summary>
/// PROB-03: computes a probation record's review schedule from its dates and the company's
/// configured checkpoint days, replacing the previous "divide probation into thirds" logic
/// (ManagerCheckIn at 1/3, HrReview at 2/3, FinalDecision at the end) that used to live directly
/// in <c>GenerateDueProbationReviewsJob</c>.
///
/// Documented review-type mapping for a checkpoint schedule (checkpoint days are offsets in days
/// from <c>StartDate</c>, e.g. the default [30, 60, 90]):
///   - Checkpoints are considered in ascending day order.
///   - A checkpoint is only used if its resolved due date falls strictly before
///     <c>ExpectedEndDate</c> — short probation periods therefore never generate a checkpoint
///     review that lands on or after the end date (see "short probation rule" below).
///   - The first surviving checkpoint becomes the ManagerCheckIn review.
///   - The second surviving checkpoint becomes the HrReview review.
///   - Any further configured checkpoints (beyond the first two) are currently ignored: there are
///     only two non-final review types (ManagerCheckIn, HrReview), so a third+ checkpoint has no
///     distinct type to map onto without creating duplicate reviews of the same type, which both
///     the "duplicate exists" check in CreateProbationReviewHandler and the per-type dedup in
///     GenerateDueProbationReviewsJob forbid. They are reserved for a future review-type
///     expansion.
///   - The FinalDecision review is never one of the numbered checkpoints — it is always scheduled
///     separately, exactly on <c>ExpectedEndDate</c>. This is what "final decision aligns with the
///     expected end date" means for PROB-03.
///
/// Short probation rule: if a configured checkpoint's day offset would fall on or after
/// <c>ExpectedEndDate</c>, that checkpoint is skipped entirely rather than clamped — clamping it
/// to the end date would either duplicate the FinalDecision review's due date or create a
/// checkpoint review that fires after probation has already concluded, both of which are the
/// "nonsensical or duplicate reviews" the ticket calls out. In the extreme case (probation shorter
/// than every configured checkpoint) only the FinalDecision review is generated.
/// </summary>
internal static class ProbationReviewScheduler
{
    public static IReadOnlyList<(ProbationReviewType ReviewType, DateOnly DueDate)> BuildSchedule(
        DateOnly startDate,
        DateOnly expectedEndDate,
        IReadOnlyList<int> checkpointDays)
    {
        var schedule = new List<(ProbationReviewType ReviewType, DateOnly DueDate)>();

        var survivingCheckpoints = checkpointDays
            .Where(day => day > 0)
            .Distinct()
            .OrderBy(day => day)
            .Select(day => startDate.AddDays(day))
            .Where(dueDate => dueDate < expectedEndDate)
            .Take(2)
            .ToList();

        var checkpointTypes = new[] { ProbationReviewType.ManagerCheckIn, ProbationReviewType.HrReview };

        for (var i = 0; i < survivingCheckpoints.Count; i++)
            schedule.Add((checkpointTypes[i], survivingCheckpoints[i]));

        schedule.Add((ProbationReviewType.FinalDecision, expectedEndDate));

        return schedule;
    }
}
