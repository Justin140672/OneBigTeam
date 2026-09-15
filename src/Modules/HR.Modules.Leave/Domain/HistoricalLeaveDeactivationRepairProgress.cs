namespace HR.Modules.Leave.Domain;

/// <summary>
/// Round 3 reliability fix (Gap-2): durable resume cursor for
/// <see cref="Jobs.ReconcileHistoricalLeaveDeactivationsJob"/>, the bounded, paginated historical
/// backlog sweep that complements <see cref="Jobs.ReconcileMissingLeaveDeactivationsJob"/>'s tight
/// 30-day lookback. That job can never see a stranded departure finalised more than 30 days ago (e.g.
/// from an extended outage of the recovery job, or a departure stranded before this whole recovery
/// mechanism existed) — this job authoritatively walks the ENTIRE finalised-departure history exactly
/// once, in bounded batches, via <see cref="Contracts.IFinalisedEmployeeDeparturesReader.GetFinalisedDeparturesPageAsync"/>.
///
/// Singleton row (see <see cref="SingletonId"/>) — this repair is a one-time global sweep, not
/// per-company, so a single cursor is sufficient. Persisting progress after every processed batch
/// means an interrupted sweep (crash, deploy, job timeout) resumes from roughly where it left off on
/// the next scheduled run rather than restarting the full history from scratch.
/// </summary>
internal sealed class HistoricalLeaveDeactivationRepairProgress
{
    private HistoricalLeaveDeactivationRepairProgress() { }

    /// <summary>Fixed, well-known id — there is only ever one row in this table.</summary>
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-00000000CAFE");

    public Guid Id { get; private set; }

    /// <summary>Keyset resume cursor: FinalisationCompletedAt of the last departure processed by the
    /// previous batch. Null means the sweep has not started (or was reset).</summary>
    public DateTimeOffset? LastProcessedFinalisationCompletedAt { get; private set; }

    /// <summary>Secondary keyset component (EmployeeId), needed because FinalisationCompletedAt is
    /// not unique across departures.</summary>
    public Guid? LastProcessedEmployeeId { get; private set; }

    /// <summary>Set once a full pass over the entire finalised-departure history has completed with
    /// no more candidates left — the job becomes a no-op on subsequent runs once true, since this is
    /// a one-time historical backlog sweep, not an ongoing recurring reconciliation (that role is
    /// already filled by <see cref="Jobs.ReconcileMissingLeaveDeactivationsJob"/> for new departures
    /// going forward).</summary>
    public bool IsComplete { get; private set; }

    public int TotalRepaired { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static HistoricalLeaveDeactivationRepairProgress CreateNew(DateTimeOffset now) => new()
    {
        Id = SingletonId,
        IsComplete = false,
        TotalRepaired = 0,
        UpdatedAt = now,
    };

    public void Advance(DateTimeOffset lastFinalisationCompletedAt, Guid lastEmployeeId, int repairedInBatch, DateTimeOffset now)
    {
        LastProcessedFinalisationCompletedAt = lastFinalisationCompletedAt;
        LastProcessedEmployeeId = lastEmployeeId;
        TotalRepaired += repairedInBatch;
        UpdatedAt = now;
    }

    public void MarkComplete(DateTimeOffset now)
    {
        IsComplete = true;
        UpdatedAt = now;
    }
}
