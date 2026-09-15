namespace HR.Modules.Employees.Domain;

/// <summary>
/// A durable, module-scoped record of an EmployeeManagerChangedIntegrationEvent that must still be
/// published for a direct report reassigned during EmployeeDepartureFinalizer's manager-departure
/// cascade (see Services/EmployeeDepartureFinalizer.cs CascadeManagerDepartureAsync).
///
/// Reliability fix: CascadeManagerDepartureAsync previously saved the report's new ManagerId and
/// only then published the integration event per report in a loop. If the process was interrupted
/// after the save but before (or partway through) that loop, the report's ManagerId already pointed
/// at the new manager, so the idempotency guard used to decide whether a report still needs
/// reassigning (ManagerId != departing employee) would skip it on any retry — silently losing the
/// event forever, even though consumers (e.g. Probation's ManagerChangedHandler) never learned about
/// the change.
///
/// This record is written in the SAME SaveChangesAsync call as the report's ManagerId reassignment,
/// capturing the previous/new manager values at that moment (never re-derived afterwards — the
/// report's current ManagerId is exactly what could no longer be trusted to recover them). A
/// background reconciliation job (Jobs/ReconcilePendingManagerChangedEventsJob.cs) publishes any
/// record still missing PublishedAt and marks it done — safe to run repeatedly since publishing an
/// already-published event a second time is prevented by checking PublishedAt first, and downstream
/// consumers of EmployeeManagerChangedIntegrationEvent are themselves expected to be idempotent
/// (see ManagerChangedHandler).
/// </summary>
internal sealed class PendingManagerChangedEvent
{
    private PendingManagerChangedEvent() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ReportEmployeeId { get; private set; }
    public Guid? PreviousManagerId { get; private set; }
    public Guid? NewManagerId { get; private set; }
    public Guid LeavingProcessId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    public static PendingManagerChangedEvent Create(
        Guid id,
        Guid companyId,
        Guid reportEmployeeId,
        Guid? previousManagerId,
        Guid? newManagerId,
        Guid leavingProcessId,
        DateTimeOffset occurredAt,
        DateTimeOffset now)
    {
        return new PendingManagerChangedEvent
        {
            Id = id,
            CompanyId = companyId,
            ReportEmployeeId = reportEmployeeId,
            PreviousManagerId = previousManagerId,
            NewManagerId = newManagerId,
            LeavingProcessId = leavingProcessId,
            OccurredAt = occurredAt,
            CreatedAt = now,
        };
    }

    /// <summary>No-op if already published — safe for the reconciliation job to call again on a
    /// record it or a concurrent run has already finished.</summary>
    public void MarkPublished(DateTimeOffset now)
    {
        if (PublishedAt is not null)
            return;

        PublishedAt = now;
    }
}
