using HR.Modules.Sickness.Domain;

namespace HR.Modules.Sickness.Services;

internal sealed record AttendanceAlertCandidate(
    AttendanceAlertRule Rule,
    DateOnly EvidencePeriodStart,
    DateOnly EvidencePeriodEnd,
    int OccurrenceCount,
    string Description);
