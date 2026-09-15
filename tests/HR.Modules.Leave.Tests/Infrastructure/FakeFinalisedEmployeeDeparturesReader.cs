using HR.Modules.Employees.Contracts;

namespace HR.Modules.Leave.Tests.Infrastructure;

/// <summary>
/// In-memory stand-in for the cross-module <see cref="IFinalisedEmployeeDeparturesReader"/>
/// contract, so ReconcileMissingLeaveDeactivationsJobTests can control exactly which finalised
/// departures the job "sees" (and applies its own lookback filtering to) without a real Employees
/// module DbContext.
/// </summary>
internal sealed class FakeFinalisedEmployeeDeparturesReader : IFinalisedEmployeeDeparturesReader
{
    private readonly List<FinalisedEmployeeDeparture> _departures = [];

    public void Add(FinalisedEmployeeDeparture departure) => _departures.Add(departure);

    public Task<IReadOnlyList<FinalisedEmployeeDeparture>> GetFinalisedDeparturesSinceAsync(
        DateTimeOffset since, CancellationToken cancellationToken)
    {
        IReadOnlyList<FinalisedEmployeeDeparture> result = _departures
            .Where(d => d.FinalisationCompletedAt >= since)
            .ToList();
        return Task.FromResult(result);
    }
}
