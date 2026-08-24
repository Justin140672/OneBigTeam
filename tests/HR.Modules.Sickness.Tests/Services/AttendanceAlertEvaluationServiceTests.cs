using HR.Modules.Companies.Contracts;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Services;

namespace HR.Modules.Sickness.Tests.Services;

/// <summary>
/// SICK-04: unit tests for the pure, deterministic AttendanceAlertEvaluationService. All tests use
/// a fixed EvaluationDate of 2026-06-15 and settings mirroring CompanySicknessSettings.Default
/// unless a specific threshold is being pinned to its boundary.
/// </summary>
public class AttendanceAlertEvaluationServiceTests
{
    private static readonly DateOnly EvaluationDate = new(2026, 6, 15);
    private const string ConfidentialMarker = "CONFIDENTIAL-MEDICAL-DETAIL";

    private static readonly CompanySicknessSettings DefaultSettings = new(
        ExcludePublicHolidaysFromSickness: false,
        FitNoteRequiredAfterDays: 7,
        ReturnToWorkRequiredAfterDays: 1,
        FrequentAbsenceCountThreshold: 4,
        FrequentAbsenceWindowDays: 365,
        LongAbsenceDayThreshold: 28,
        WeekdayPatternOccurrenceThreshold: 3,
        WeekdayPatternWindowDays: 365);

    private static AttendanceAlertEvaluationService BuildService() => new();

    private static SicknessRecord CreateRecord(
        DateOnly startDate,
        DateOnly? endDate = null,
        string? notes = null,
        SicknessStatus? forceStatus = null)
    {
        var record = SicknessRecord.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            startDate,
            SicknessDayPart.FullDay,
            endDate,
            endDate is null ? null : SicknessDayPart.FullDay,
            totalDays: endDate is null ? null : 1m,
            notes,
            SicknessEvidenceStatus.NotRequired,
            new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

        return record;
    }

    private static ReturnToWorkReview CreatePendingReview(
        Guid sicknessRecordId, Guid employeeId, DateOnly dueDate, DateTimeOffset now) =>
        ReturnToWorkReview.Create(Guid.NewGuid(), Guid.NewGuid(), sicknessRecordId, employeeId, dueDate, now);

    private static ReturnToWorkReview CreateOverdueReview(
        Guid sicknessRecordId, Guid employeeId, DateOnly dueDate, DateTimeOffset now)
    {
        var review = CreatePendingReview(sicknessRecordId, employeeId, dueDate, now);
        review.MarkOverdue(now);
        return review;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FrequentAbsences
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_FrequentAbsences_Fires_At_ExactThreshold()
    {
        var records = new[]
        {
            CreateRecord(new DateOnly(2026, 1, 5)),
            CreateRecord(new DateOnly(2026, 2, 5)),
            CreateRecord(new DateOnly(2026, 3, 5)),
            CreateRecord(new DateOnly(2026, 4, 5)),
        };

        var candidates = BuildService().Evaluate(records, [], DefaultSettings, EvaluationDate);

        var candidate = Assert.Single(candidates, c => c.Rule == AttendanceAlertRule.FrequentAbsences);
        Assert.Equal(4, candidate.OccurrenceCount);
        Assert.Equal(new DateOnly(2026, 1, 5), candidate.EvidencePeriodStart);
        Assert.Equal(EvaluationDate, candidate.EvidencePeriodEnd);
    }

    [Fact]
    public void Evaluate_FrequentAbsences_DoesNotFire_OneBelowThreshold()
    {
        var records = new[]
        {
            CreateRecord(new DateOnly(2026, 1, 5)),
            CreateRecord(new DateOnly(2026, 2, 5)),
            CreateRecord(new DateOnly(2026, 3, 5)),
        };

        var candidates = BuildService().Evaluate(records, [], DefaultSettings, EvaluationDate);

        Assert.DoesNotContain(candidates, c => c.Rule == AttendanceAlertRule.FrequentAbsences);
    }

    [Fact]
    public void Evaluate_FrequentAbsences_Excludes_SpellsOutsideRollingWindow()
    {
        // Window is 365 days ending on EvaluationDate (2026-06-15) -> window start 2025-06-15.
        // One spell falls just outside the window and must not count toward the threshold.
        var records = new[]
        {
            CreateRecord(new DateOnly(2025, 6, 14)), // outside window (one day too early)
            CreateRecord(new DateOnly(2026, 2, 5)),
            CreateRecord(new DateOnly(2026, 3, 5)),
            CreateRecord(new DateOnly(2026, 4, 5)),
        };

        var candidates = BuildService().Evaluate(records, [], DefaultSettings, EvaluationDate);

        Assert.DoesNotContain(candidates, c => c.Rule == AttendanceAlertRule.FrequentAbsences);
    }

    [Fact]
    public void Evaluate_FrequentAbsences_Includes_SpellExactlyAtWindowStart()
    {
        var windowStart = EvaluationDate.AddDays(-365);
        var records = new[]
        {
            CreateRecord(windowStart), // exactly at window boundary — inclusive
            CreateRecord(new DateOnly(2026, 2, 5)),
            CreateRecord(new DateOnly(2026, 3, 5)),
            CreateRecord(new DateOnly(2026, 4, 5)),
        };

        var candidates = BuildService().Evaluate(records, [], DefaultSettings, EvaluationDate);

        var candidate = Assert.Single(candidates, c => c.Rule == AttendanceAlertRule.FrequentAbsences);
        Assert.Equal(4, candidate.OccurrenceCount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // WeekdayPattern
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_WeekdayPattern_Fires_At_ExactThreshold()
    {
        // 2026-01-05, 2026-01-12, 2026-01-19 are all Mondays.
        var records = new[]
        {
            CreateRecord(new DateOnly(2026, 1, 5)),
            CreateRecord(new DateOnly(2026, 1, 12)),
            CreateRecord(new DateOnly(2026, 1, 19)),
        };

        var candidates = BuildService().Evaluate(records, [], DefaultSettings, EvaluationDate);

        var candidate = Assert.Single(candidates, c => c.Rule == AttendanceAlertRule.WeekdayPattern);
        Assert.Equal(3, candidate.OccurrenceCount);
        Assert.Equal(new DateOnly(2026, 1, 5), candidate.EvidencePeriodStart);
    }

    [Fact]
    public void Evaluate_WeekdayPattern_DoesNotFire_OneBelowThreshold()
    {
        var records = new[]
        {
            CreateRecord(new DateOnly(2026, 1, 5)),
            CreateRecord(new DateOnly(2026, 1, 12)),
        };

        var candidates = BuildService().Evaluate(records, [], DefaultSettings, EvaluationDate);

        Assert.DoesNotContain(candidates, c => c.Rule == AttendanceAlertRule.WeekdayPattern);
    }

    [Fact]
    public void Evaluate_WeekdayPattern_DoesNotFire_WhenSameCountSpreadAcrossDifferentWeekdays()
    {
        // Three absences on three *different* weekdays must not trip a same-weekday pattern.
        var records = new[]
        {
            CreateRecord(new DateOnly(2026, 1, 5)),  // Monday
            CreateRecord(new DateOnly(2026, 1, 13)), // Tuesday
            CreateRecord(new DateOnly(2026, 1, 21)), // Wednesday
        };

        var candidates = BuildService().Evaluate(records, [], DefaultSettings, EvaluationDate);

        Assert.DoesNotContain(candidates, c => c.Rule == AttendanceAlertRule.WeekdayPattern);
    }

    [Fact]
    public void Evaluate_WeekdayPattern_Excludes_OccurrencesOutsideRollingWindow()
    {
        var windowStart = EvaluationDate.AddDays(-365);
        var records = new[]
        {
            CreateRecord(windowStart.AddDays(-7)), // one week before window start, same weekday, excluded
            CreateRecord(new DateOnly(2026, 1, 12)),
            CreateRecord(new DateOnly(2026, 1, 19)),
        };

        var candidates = BuildService().Evaluate(records, [], DefaultSettings, EvaluationDate);

        Assert.DoesNotContain(candidates, c => c.Rule == AttendanceAlertRule.WeekdayPattern);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // LongAbsence
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_LongAbsence_Fires_At_ExactThreshold_ClosedRecord()
    {
        var start = new DateOnly(2026, 1, 1);
        var end = start.AddDays(27); // inclusive of both ends => 28 calendar days
        var records = new[] { CreateRecord(start, end) };

        var candidates = BuildService().Evaluate(records, [], DefaultSettings, EvaluationDate);

        var candidate = Assert.Single(candidates, c => c.Rule == AttendanceAlertRule.LongAbsence);
        Assert.Equal(28, candidate.OccurrenceCount);
        Assert.Equal(start, candidate.EvidencePeriodStart);
        Assert.Equal(end, candidate.EvidencePeriodEnd);
    }

    [Fact]
    public void Evaluate_LongAbsence_DoesNotFire_OneDayBelowThreshold()
    {
        var start = new DateOnly(2026, 1, 1);
        var end = start.AddDays(26); // 27 calendar days — one below the 28-day threshold
        var records = new[] { CreateRecord(start, end) };

        var candidates = BuildService().Evaluate(records, [], DefaultSettings, EvaluationDate);

        Assert.DoesNotContain(candidates, c => c.Rule == AttendanceAlertRule.LongAbsence);
    }

    [Fact]
    public void Evaluate_LongAbsence_OpenRecord_MeasuredAgainstEvaluationDate()
    {
        // Open record (EndDate == null) started exactly 28 calendar days before EvaluationDate.
        var start = EvaluationDate.AddDays(-27);
        var records = new[] { CreateRecord(start, endDate: null) };

        var candidates = BuildService().Evaluate(records, [], DefaultSettings, EvaluationDate);

        var candidate = Assert.Single(candidates, c => c.Rule == AttendanceAlertRule.LongAbsence);
        Assert.Equal(28, candidate.OccurrenceCount);
        Assert.Equal(EvaluationDate, candidate.EvidencePeriodEnd);
    }

    [Fact]
    public void Evaluate_LongAbsence_OpenRecord_OneDayBelowThreshold_DoesNotFire()
    {
        var start = EvaluationDate.AddDays(-26); // only 27 calendar days elapsed as of EvaluationDate
        var records = new[] { CreateRecord(start, endDate: null) };

        var candidates = BuildService().Evaluate(records, [], DefaultSettings, EvaluationDate);

        Assert.DoesNotContain(candidates, c => c.Rule == AttendanceAlertRule.LongAbsence);
    }

    [Fact]
    public void Evaluate_LongAbsence_FiresPerQualifyingSpell_WhenMultiplePresent()
    {
        var records = new[]
        {
            CreateRecord(new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 28)),
            CreateRecord(new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 28)),
        };

        var candidates = BuildService().Evaluate(records, [], DefaultSettings, EvaluationDate);

        Assert.Equal(2, candidates.Count(c => c.Rule == AttendanceAlertRule.LongAbsence));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MissingReturnToWorkReview
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_MissingReturnToWorkReview_Fires_ForOverdueStatusReview()
    {
        var record = CreateRecord(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3));
        var now = new DateTimeOffset(2026, 5, 4, 0, 0, 0, TimeSpan.Zero);
        var review = CreateOverdueReview(record.Id, Guid.NewGuid(), new DateOnly(2026, 5, 4), now);

        var candidates = BuildService().Evaluate([record], [review], DefaultSettings, EvaluationDate);

        Assert.Single(candidates, c => c.Rule == AttendanceAlertRule.MissingReturnToWorkReview);
    }

    [Fact]
    public void Evaluate_MissingReturnToWorkReview_Fires_ForPendingReviewPastDueDate()
    {
        var record = CreateRecord(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3));
        var now = new DateTimeOffset(2026, 5, 4, 0, 0, 0, TimeSpan.Zero);
        // DueDate before EvaluationDate (2026-06-15) but review never got transitioned to Overdue.
        var review = CreatePendingReview(record.Id, Guid.NewGuid(), new DateOnly(2026, 5, 4), now);

        var candidates = BuildService().Evaluate([record], [review], DefaultSettings, EvaluationDate);

        Assert.Single(candidates, c => c.Rule == AttendanceAlertRule.MissingReturnToWorkReview);
    }

    [Fact]
    public void Evaluate_MissingReturnToWorkReview_DoesNotFire_ForPendingReviewNotYetDue()
    {
        var record = CreateRecord(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 12));
        var now = new DateTimeOffset(2026, 6, 12, 0, 0, 0, TimeSpan.Zero);
        var review = CreatePendingReview(record.Id, Guid.NewGuid(), EvaluationDate.AddDays(1), now);

        var candidates = BuildService().Evaluate([record], [review], DefaultSettings, EvaluationDate);

        Assert.DoesNotContain(candidates, c => c.Rule == AttendanceAlertRule.MissingReturnToWorkReview);
    }

    [Fact]
    public void Evaluate_MissingReturnToWorkReview_DoesNotFire_ForPendingReviewDueExactlyOnEvaluationDate()
    {
        // DueDate < evaluationDate is the overdue condition — due *on* the evaluation date is not yet overdue.
        var record = CreateRecord(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 12));
        var now = new DateTimeOffset(2026, 6, 12, 0, 0, 0, TimeSpan.Zero);
        var review = CreatePendingReview(record.Id, Guid.NewGuid(), EvaluationDate, now);

        var candidates = BuildService().Evaluate([record], [review], DefaultSettings, EvaluationDate);

        Assert.DoesNotContain(candidates, c => c.Rule == AttendanceAlertRule.MissingReturnToWorkReview);
    }

    [Fact]
    public void Evaluate_MissingReturnToWorkReview_DoesNotFire_ForCompletedReview()
    {
        var record = CreateRecord(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3));
        var now = new DateTimeOffset(2026, 5, 4, 0, 0, 0, TimeSpan.Zero);
        var review = CreatePendingReview(record.Id, Guid.NewGuid(), new DateOnly(2026, 5, 4), now);
        review.Complete(Guid.NewGuid(), FitToReturnOutcome.Fit, false, null, null, now);

        var candidates = BuildService().Evaluate([record], [review], DefaultSettings, EvaluationDate);

        Assert.DoesNotContain(candidates, c => c.Rule == AttendanceAlertRule.MissingReturnToWorkReview);
    }

    [Fact]
    public void Evaluate_MissingReturnToWorkReview_Fires_ForClosedRecordWithNoReviewAtAll()
    {
        var record = CreateRecord(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)); // 3 days, >= ReturnToWorkRequiredAfterDays(1)

        var candidates = BuildService().Evaluate([record], [], DefaultSettings, EvaluationDate);

        var candidate = Assert.Single(candidates, c => c.Rule == AttendanceAlertRule.MissingReturnToWorkReview);
        Assert.Equal(record.EndDate, candidate.EvidencePeriodStart);
    }

    [Fact]
    public void Evaluate_MissingReturnToWorkReview_DoesNotFire_ForOpenRecordWithNoReview()
    {
        var record = CreateRecord(new DateOnly(2026, 5, 1), endDate: null);

        var candidates = BuildService().Evaluate([record], [], DefaultSettings, EvaluationDate);

        Assert.DoesNotContain(candidates, c => c.Rule == AttendanceAlertRule.MissingReturnToWorkReview);
    }

    [Fact]
    public void Evaluate_MissingReturnToWorkReview_DoesNotFire_ForClosedRecord_AlreadyHasReview()
    {
        var record = CreateRecord(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3));
        var now = new DateTimeOffset(2026, 5, 4, 0, 0, 0, TimeSpan.Zero);
        // Pending, not yet due — the review exists so the "missing review" catch-all must not
        // also fire for the same record.
        var review = CreatePendingReview(record.Id, Guid.NewGuid(), EvaluationDate.AddDays(1), now);

        var candidates = BuildService().Evaluate([record], [review], DefaultSettings, EvaluationDate);

        Assert.DoesNotContain(candidates, c => c.Rule == AttendanceAlertRule.MissingReturnToWorkReview);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Sensitive-data exclusion (all rules)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_Descriptions_NeverContain_SicknessRecordNotesOrReviewNotes()
    {
        var records = new List<SicknessRecord>
        {
            CreateRecord(new DateOnly(2026, 1, 5), notes: ConfidentialMarker),
            CreateRecord(new DateOnly(2026, 1, 12), notes: ConfidentialMarker),
            CreateRecord(new DateOnly(2026, 1, 19), notes: ConfidentialMarker),
            CreateRecord(new DateOnly(2026, 2, 5), notes: ConfidentialMarker),
        };

        var longRecord = CreateRecord(new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 28), notes: ConfidentialMarker);
        records.Add(longRecord);

        var missingReviewRecord = CreateRecord(new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 5), notes: ConfidentialMarker);
        records.Add(missingReviewRecord);

        var now = new DateTimeOffset(2026, 5, 4, 0, 0, 0, TimeSpan.Zero);
        var overdueRecord = CreateRecord(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3));
        records.Add(overdueRecord);
        var overdueReview = CreateOverdueReview(overdueRecord.Id, Guid.NewGuid(), new DateOnly(2026, 5, 4), now);

        var candidates = BuildService().Evaluate(records, [overdueReview], DefaultSettings, EvaluationDate);

        Assert.NotEmpty(candidates);
        Assert.All(candidates, c => Assert.DoesNotContain(ConfidentialMarker, c.Description));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Determinism / repeat-call
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_IsDeterministic_AcrossRepeatedCalls()
    {
        var records = new[]
        {
            CreateRecord(new DateOnly(2026, 1, 5)),
            CreateRecord(new DateOnly(2026, 2, 5)),
            CreateRecord(new DateOnly(2026, 3, 5)),
            CreateRecord(new DateOnly(2026, 4, 5)),
        };

        var service = BuildService();
        var first = service.Evaluate(records, [], DefaultSettings, EvaluationDate);
        var second = service.Evaluate(records, [], DefaultSettings, EvaluationDate);

        Assert.Equal(first, second);
    }
}
