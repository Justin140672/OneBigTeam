namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Ticket 4: central, tunable resource budgets for the organisation data export build pipeline.
/// Keeps the concurrency cap, temp-disk ceilings and queue-wait in one place so operations can
/// adjust them without hunting through the job. Values are deliberately conservative defaults;
/// see specifications/implemented-tickets/ticket-4-export-resource-limits.md for the sizing rationale.
/// </summary>
public sealed class OrganisationDataExportResourceLimits
{
    public int MaxConcurrentExports { get; init; } = 2;

    public TimeSpan SlotAcquireTimeout { get; init; } = TimeSpan.FromMinutes(10);

    public long MaxArchiveBytesPerExport { get; init; } = 12L * 1024 * 1024 * 1024;

    public long MaxTotalWorkspaceBytes { get; init; } = 26L * 1024 * 1024 * 1024;

    public long MinimumFreeDiskBytes { get; init; } = 14L * 1024 * 1024 * 1024;

    public TimeSpan OrphanWorkspaceAge { get; init; } = TimeSpan.FromHours(6);

    public static OrganisationDataExportResourceLimits Default { get; } = new();
}
