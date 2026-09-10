namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Ticket 4: caps the number of organisation data export builds running concurrently in a worker
/// process. A build must hold a slot for its entire duration (read, compress, upload) and release it
/// on every exit path. When all slots are busy the caller waits up to
/// <see cref="OrganisationDataExportResourceLimits.SlotAcquireTimeout"/>; if none frees up it gets
/// back <c>null</c> and must abandon this run without claiming an attempt (Hangfire re-queues it).
/// Process-scoped by design — see the design note for the multi-worker follow-up.
/// </summary>
public interface IOrganisationDataExportConcurrencyGate
{
    int MaxConcurrentExports { get; }

    /// <summary>
    /// Waits for a free slot. Returns a handle whose disposal releases the slot, or <c>null</c> if no
    /// slot became available within the configured timeout.
    /// </summary>
    Task<IAsyncDisposable?> AcquireAsync(CancellationToken cancellationToken);
}

/// <summary>Ticket 4: thrown when a build cannot get a concurrency slot; surfaces to Hangfire for a delayed retry.</summary>
public sealed class OrganisationDataExportSlotUnavailableException(Guid exportId)
    : Exception($"No organisation data export build slot became available for export '{exportId}'.")
{
    public Guid ExportId { get; } = exportId;
}

/// <summary>Default process-wide implementation backed by a <see cref="SemaphoreSlim"/>. Register as a singleton.</summary>
public sealed class OrganisationDataExportConcurrencyGate : IOrganisationDataExportConcurrencyGate
{
    private readonly SemaphoreSlim _slots;
    private readonly TimeSpan _acquireTimeout;

    public OrganisationDataExportConcurrencyGate(OrganisationDataExportResourceLimits? limits = null)
    {
        limits ??= OrganisationDataExportResourceLimits.Default;
        MaxConcurrentExports = Math.Max(1, limits.MaxConcurrentExports);
        _slots = new SemaphoreSlim(MaxConcurrentExports, MaxConcurrentExports);
        _acquireTimeout = limits.SlotAcquireTimeout;
    }

    public int MaxConcurrentExports { get; }

    public async Task<IAsyncDisposable?> AcquireAsync(CancellationToken cancellationToken)
    {
        if (!await _slots.WaitAsync(_acquireTimeout, cancellationToken))
            return null;

        return new Slot(_slots);
    }

    private sealed class Slot(SemaphoreSlim slots) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                slots.Release();
            return ValueTask.CompletedTask;
        }
    }
}
