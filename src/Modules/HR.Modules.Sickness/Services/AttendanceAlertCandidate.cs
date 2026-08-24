using HR.Modules.Sickness.Domain;

namespace HR.Modules.Sickness.Services;

/// <summary>
/// A candidate alert produced by <see cref="AttendanceAlertEvaluationService"/>. Pure data — no
/// persistence, no side effects. Consumers (e.g. AttendanceAlertEvaluationJob) are responsible for
/// duplicate-checking against existing <see cref="AttendanceAlert"/> rows before insert.
/// </summary>
internal sealed record AttendanceAlertCandidate(
    AttendanceAlertRule Rule,
    DateOnly EvidencePeriodStart,
    DateOnly EvidencePeriodEnd,
    int OccurrenceCount,
    string Description);
