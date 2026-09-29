using HR.Modules.Employees.Contracts;

namespace HR.Modules.Leave.Tests.Infrastructure;

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

    public Task<IReadOnlyList<FinalisedEmployeeDeparture>> GetFinalisedDeparturesPageAsync(
        DateTimeOffset? afterFinalisationCompletedAt,
        Guid? afterEmployeeId,
        int take,
        CancellationToken cancellationToken)
    {
        var ordered = _departures
            .OrderBy(d => d.FinalisationCompletedAt)
            .ThenBy(d => d.EmployeeId);

        if (afterFinalisationCompletedAt is not null && afterEmployeeId is not null)
        {
            var afterCompleted = afterFinalisationCompletedAt.Value;
            var afterEmployee = afterEmployeeId.Value;

            ordered = ordered.Where(d =>
                    d.FinalisationCompletedAt > afterCompleted
                    || (d.FinalisationCompletedAt == afterCompleted && d.EmployeeId.CompareTo(afterEmployee) > 0))
                .OrderBy(d => d.FinalisationCompletedAt)
                .ThenBy(d => d.EmployeeId);
        }

        IReadOnlyList<FinalisedEmployeeDeparture> result = ordered.Take(take).ToList();
        return Task.FromResult(result);
    }
}
