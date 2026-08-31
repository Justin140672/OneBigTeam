namespace HR.Modules.Recruitment.Features.GetNewApplicationsMetric;

internal sealed record GetNewApplicationsMetricRequest
{
    public Guid CompanyId { get; init; }

    // DSH-04: only used for the fallback definition (no stage is flagged with the NewApplication
    // purpose) — applications received within this many days are then treated as "new".
    // Defaults to 14 in the handler when not supplied.
    public int? NewWithinDays { get; init; }
}
