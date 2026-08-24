namespace HR.Modules.Sickness.Domain;

/// <summary>
/// SICK-04: an informational attendance-pattern alert. Alerts are generated deterministically by
/// <see cref="Services.AttendanceAlertEvaluationService"/> and are purely observational — creating
/// one never mutates <see cref="SicknessRecord"/>, employment or disciplinary state, and nothing in
/// this module reads AttendanceAlert to drive automatic disciplinary action.
///
/// Deliberately excludes any clinical detail: <see cref="Description"/> is built only from dates
/// and counts (see AttendanceAlertEvaluationService), and this entity has no reference to
/// SicknessRecord.Notes/EvidenceNotes or SicknessCategory. Do not add such a reference.
/// </summary>
internal sealed class AttendanceAlert
{
    private AttendanceAlert() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public AttendanceAlertRule Rule { get; private set; }

    /// <summary>The evidence window the alert was raised from — used for duplicate prevention (SICK-04: no
    /// repeated alert for the same employee+rule+window) and for scoping what dates are displayed.</summary>
    public DateOnly EvidencePeriodStart { get; private set; }
    public DateOnly EvidencePeriodEnd { get; private set; }

    /// <summary>Count backing the pattern (spell count, weekday occurrence count, or 1 for LongAbsence/
    /// MissingReturnToWorkReview) — the only figure shown to managers under the reduced view (see
    /// Features/ListAttendanceAlerts).</summary>
    public int OccurrenceCount { get; private set; }

    /// <summary>Human-readable explanation built only from dates/counts — never medical notes, evidence
    /// notes, or sickness category. HR-only field (see Features/ListAttendanceAlerts).</summary>
    public string Description { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static AttendanceAlert Create(
        Guid id,
        Guid companyId,
        Guid employeeId,
        AttendanceAlertRule rule,
        DateOnly evidencePeriodStart,
        DateOnly evidencePeriodEnd,
        int occurrenceCount,
        string description,
        DateTimeOffset now)
    {
        return new AttendanceAlert
        {
            Id = id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            Rule = rule,
            EvidencePeriodStart = evidencePeriodStart,
            EvidencePeriodEnd = evidencePeriodEnd,
            OccurrenceCount = occurrenceCount,
            Description = description,
            CreatedAt = now,
        };
    }
}
