namespace HR.Modules.Employees.Features.BackfillEmployeeTimeline;

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
