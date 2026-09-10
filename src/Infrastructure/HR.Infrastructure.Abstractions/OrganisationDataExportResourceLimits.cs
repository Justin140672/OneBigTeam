namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Ticket 4: central, tunable resource budgets for the organisation data export build pipeline.
/// Keeps the concurrency cap, temp-disk ceilings and queue-wait in one place so operations can
/// adjust them without hunting through the job. Values are deliberately conservative defaults;
/// see docs/tickets/ticket-4-export-resource-limits.md for the sizing rationale.
/// </summary>
public sealed class OrganisationDataExportResourceLimits
{
    /// <summary>Maximum export builds allowed to run at once in a single worker process.</summary>
    public int MaxConcurrentExports { get; init; } = 2;

    /// <summary>How long a build waits for a concurrency slot before giving up (and letting Hangfire re-queue it).</summary>
    public TimeSpan SlotAcquireTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Hard ceiling on the size of a single in-progress export archive on temp disk.</summary>
    public long MaxArchiveBytesPerExport { get; init; } = 12L * 1024 * 1024 * 1024;

    /// <summary>Ceiling on the combined size of all in-progress export archives in the work root.</summary>
    public long MaxTotalWorkspaceBytes { get; init; } = 26L * 1024 * 1024 * 1024;

    /// <summary>A new workspace is refused unless the drive has at least this much free space.</summary>
    public long MinimumFreeDiskBytes { get; init; } = 14L * 1024 * 1024 * 1024;

    /// <summary>Workspace directories older than this are treated as orphaned by a process kill and swept.</summary>
    public TimeSpan OrphanWorkspaceAge { get; init; } = TimeSpan.FromHours(6);

    public static OrganisationDataExportResourceLimits Default { get; } = new();
}
