namespace HR.Modules.Employees.Contracts;

// Gap-2 reliability fix: EmployeeDepartureFinalisedIntegrationEvent is dispatched in-process by
// HR.SharedKernel.IntegrationEventPublisher, which deliberately swallows and only logs a handler's
// own exception. If a required consumer (e.g. Leave's EmployeeDepartureFinalisedHandler) throws
// before it durably records its own required work, EmployeeLeavingProcess.FinalisationCompletedAt
// is still (correctly, per that property's own contract) marked complete, and that consumer's own
// existing retry/reconciliation mechanism can never find the gap because it never durably recorded
// anything to retry in the first place.
//
// This purpose-specific read contract lets a consuming module authoritatively re-derive "every
// departure Employees considers fully finalised" without querying the Employees module's schema
// directly, so it can reconcile "finalised departure exists but MY OWN required record does not"
// — a class of gap no amount of retrying an already-created record can ever catch.
public interface IFinalisedEmployeeDeparturesReader
{
    Task<IReadOnlyList<FinalisedEmployeeDeparture>> GetFinalisedDeparturesSinceAsync(
        DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>
    /// Round 3 reliability fix (Gap-2 follow-up): a bounded, keyset-paginated companion to
    /// <see cref="GetFinalisedDeparturesSinceAsync"/> for consumers that need to sweep the ENTIRE
    /// finalised-departure history (not just a recent lookback window) without ever issuing one
    /// unbounded query. Ordered by (FinalisationCompletedAt, EmployeeId) ascending across all
    /// companies. Pass the cursor of the last row processed from the previous page
    /// (<paramref name="afterFinalisationCompletedAt"/>/<paramref name="afterEmployeeId"/>, both
    /// null to start from the beginning) to resume exactly where a prior run left off. Returns up to
    /// <paramref name="take"/> rows; an empty result means no more rows exist after the given cursor.
    /// </summary>
    Task<IReadOnlyList<FinalisedEmployeeDeparture>> GetFinalisedDeparturesPageAsync(
        DateTimeOffset? afterFinalisationCompletedAt,
        Guid? afterEmployeeId,
        int take,
        CancellationToken cancellationToken);
}

public sealed record FinalisedEmployeeDeparture(
    Guid CompanyId,
    Guid EmployeeId,
    Guid LeavingProcessId,
    DateOnly LeavingDate,
    DateTimeOffset FinalisationCompletedAt);
