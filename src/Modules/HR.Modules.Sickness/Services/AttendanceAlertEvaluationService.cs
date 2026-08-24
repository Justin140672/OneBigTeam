using HR.Modules.Companies.Contracts;
using HR.Modules.Sickness.Domain;

namespace HR.Modules.Sickness.Services;

/// <summary>
/// SICK-04: deterministic, pure evaluation of attendance-pattern rules for a single employee.
/// Takes the employee's sickness history plus configured thresholds and an evaluation date, and
/// returns which rules fire. Never touches the database, never mutates SicknessRecord/
/// ReturnToWorkReview, and produces no clinical detail — descriptions are built only from dates
/// and counts. Callers (AttendanceAlertEvaluationJob) are responsible for persistence and
/// duplicate-prevention.
/// </summary>
internal sealed class AttendanceAlertEvaluationService
{
    public IReadOnlyList<AttendanceAlertCandidate> Evaluate(
        IReadOnlyList<SicknessRecord> sicknessRecords,
        IReadOnlyList<ReturnToWorkReview> returnToWorkReviews,
        CompanySicknessSettings settings,
        DateOnly evaluationDate)
    {
        var candidates = new List<AttendanceAlertCandidate>();

        var frequentAbsences = EvaluateFrequentAbsences(sicknessRecords, settings, evaluationDate);
        if (frequentAbsences is not null) candidates.Add(frequentAbsences);

        var weekdayPattern = EvaluateWeekdayPattern(sicknessRecords, settings, evaluationDate);
        if (weekdayPattern is not null) candidates.Add(weekdayPattern);

        candidates.AddRange(EvaluateLongAbsences(sicknessRecords, settings, evaluationDate));

        candidates.AddRange(EvaluateMissingReturnToWorkReviews(sicknessRecords, returnToWorkReviews, settings, evaluationDate));

        return candidates;
    }

    /// <summary>Rule: N or more separate absence spells starting within a rolling window ending on the
    /// evaluation date.</summary>
    private static AttendanceAlertCandidate? EvaluateFrequentAbsences(
        IReadOnlyList<SicknessRecord> sicknessRecords,
        CompanySicknessSettings settings,
        DateOnly evaluationDate)
    {
        var windowStart = evaluationDate.AddDays(-settings.FrequentAbsenceWindowDays);

        var spellsInWindow = sicknessRecords
            .Where(r => r.StartDate >= windowStart && r.StartDate <= evaluationDate)
            .OrderBy(r => r.StartDate)
            .ToList();

        if (spellsInWindow.Count < settings.FrequentAbsenceCountThreshold)
            return null;

        var periodStart = spellsInWindow.Min(r => r.StartDate);

        return new AttendanceAlertCandidate(
            AttendanceAlertRule.FrequentAbsences,
            periodStart,
            evaluationDate,
            spellsInWindow.Count,
            $"{spellsInWindow.Count} separate absence spells between {periodStart:yyyy-MM-dd} and {evaluationDate:yyyy-MM-dd}.");
    }

    /// <summary>Rule: a single weekday recurring as the absence start day N or more times within a
    /// rolling window.</summary>
    private static AttendanceAlertCandidate? EvaluateWeekdayPattern(
        IReadOnlyList<SicknessRecord> sicknessRecords,
        CompanySicknessSettings settings,
        DateOnly evaluationDate)
    {
        var windowStart = evaluationDate.AddDays(-settings.WeekdayPatternWindowDays);

        var spellsInWindow = sicknessRecords
            .Where(r => r.StartDate >= windowStart && r.StartDate <= evaluationDate)
            .ToList();

        var byWeekday = spellsInWindow
            .GroupBy(r => r.StartDate.DayOfWeek)
            .Select(g => new { Weekday = g.Key, Dates = g.Select(r => r.StartDate).OrderBy(d => d).ToList() })
            .Where(g => g.Dates.Count >= settings.WeekdayPatternOccurrenceThreshold)
            .OrderByDescending(g => g.Dates.Count)
            .ThenBy(g => g.Weekday)
            .FirstOrDefault();

        if (byWeekday is null)
            return null;

        var periodStart = byWeekday.Dates.First();

        return new AttendanceAlertCandidate(
            AttendanceAlertRule.WeekdayPattern,
            periodStart,
            evaluationDate,
            byWeekday.Dates.Count,
            $"{byWeekday.Dates.Count} absences starting on a {byWeekday.Weekday} between {periodStart:yyyy-MM-dd} and {evaluationDate:yyyy-MM-dd}.");
    }

    /// <summary>Rule: a single absence spell whose duration meets/exceeds the long-absence threshold.
    /// Fires per qualifying spell (a person may have more than one long spell in their history).</summary>
    private static IEnumerable<AttendanceAlertCandidate> EvaluateLongAbsences(
        IReadOnlyList<SicknessRecord> sicknessRecords,
        CompanySicknessSettings settings,
        DateOnly evaluationDate)
    {
        foreach (var record in sicknessRecords)
        {
            // An open (ongoing) spell is measured up to the evaluation date; a closed spell up to
            // its own EndDate. Either way this never looks beyond the evaluation date.
            var effectiveEnd = record.EndDate ?? evaluationDate;
            if (effectiveEnd > evaluationDate)
                continue;

            var durationDays = effectiveEnd.DayNumber - record.StartDate.DayNumber + 1;
            if (durationDays < settings.LongAbsenceDayThreshold)
                continue;

            yield return new AttendanceAlertCandidate(
                AttendanceAlertRule.LongAbsence,
                record.StartDate,
                effectiveEnd,
                durationDays,
                $"A single absence spell of {durationDays} calendar days from {record.StartDate:yyyy-MM-dd} to {effectiveEnd:yyyy-MM-dd}.");
        }
    }

    /// <summary>Rule: a return-to-work review that is overdue as of the evaluation date, or a closed
    /// sickness record whose duration required a review but no review record exists at all (a data
    /// gap — every other write path raises one, so this is a defensive catch-all).</summary>
    private static IEnumerable<AttendanceAlertCandidate> EvaluateMissingReturnToWorkReviews(
        IReadOnlyList<SicknessRecord> sicknessRecords,
        IReadOnlyList<ReturnToWorkReview> returnToWorkReviews,
        CompanySicknessSettings settings,
        DateOnly evaluationDate)
    {
        foreach (var review in returnToWorkReviews)
        {
            var isOverdue = review.Status == ReturnToWorkReviewStatus.Overdue ||
                             (review.Status == ReturnToWorkReviewStatus.Pending && review.DueDate < evaluationDate);

            if (!isOverdue)
                continue;

            yield return new AttendanceAlertCandidate(
                AttendanceAlertRule.MissingReturnToWorkReview,
                review.DueDate,
                evaluationDate,
                1,
                $"Return-to-work review due {review.DueDate:yyyy-MM-dd} is overdue as of {evaluationDate:yyyy-MM-dd}.");
        }

        var reviewedRecordIds = returnToWorkReviews.Select(r => r.SicknessRecordId).ToHashSet();

        foreach (var record in sicknessRecords)
        {
            if (record.Status != SicknessStatus.Closed || record.EndDate is null)
                continue;

            if (reviewedRecordIds.Contains(record.Id))
                continue;

            var durationDays = record.EndDate.Value.DayNumber - record.StartDate.DayNumber + 1;
            if (durationDays < settings.ReturnToWorkRequiredAfterDays)
                continue;

            yield return new AttendanceAlertCandidate(
                AttendanceAlertRule.MissingReturnToWorkReview,
                record.EndDate.Value,
                evaluationDate,
                1,
                $"Absence closed {record.EndDate.Value:yyyy-MM-dd} required a return-to-work review that was never raised.");
        }
    }
}
