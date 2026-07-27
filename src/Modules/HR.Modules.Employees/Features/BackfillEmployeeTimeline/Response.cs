namespace HR.Modules.Employees.Features.BackfillEmployeeTimeline;

// One row per timeline source this backfill covers. For the 3 in-module sources, Created/Skipped
// are exact (this handler writes the entries itself via IEmployeeTimelineWriter and observes its
// return value directly). For the 4 cross-module sources (Probation/Onboarding/Documents/
// Offboarding), Created is derived from the delta in matching EmployeeTimelineEntries before/after
// the replay call (the only way to observe write outcomes across the integration-event boundary
// without changing IIntegrationEventPublisher), and Skipped is derived as (Processed - Created)
// using the count of historical records the replayer reports having processed — see
// BackfillEmployeeTimelineHandler's remarks.
internal sealed record BackfillSourceResult(
    string Source,
    int Created,
    int Skipped,
    int Failed);

internal sealed record BackfillEmployeeTimelineResponse(
    Guid CompanyId,
    IReadOnlyList<BackfillSourceResult> Sources,
    int TotalCreated,
    int TotalSkipped,
    int TotalFailed);
