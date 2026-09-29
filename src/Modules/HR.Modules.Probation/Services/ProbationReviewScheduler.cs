using HR.Modules.Probation.Domain;

namespace HR.Modules.Probation.Services;

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
