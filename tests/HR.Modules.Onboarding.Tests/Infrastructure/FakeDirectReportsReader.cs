using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;

namespace HR.Modules.Onboarding.Tests.Infrastructure;

internal sealed class FakeDirectReportsReader : IDirectReportsReader
{
    private readonly IReadOnlyList<Guid>? _flat;
    private readonly Dictionary<Guid, HashSet<Guid>> _tree;

    public FakeDirectReportsReader(params Guid[] reportIds)
    {
        _flat = reportIds;
        _tree = new();
    }

    private FakeDirectReportsReader(Dictionary<Guid, HashSet<Guid>> tree)
    {
        _flat = null;
        _tree = tree;
    }

    public static FakeDirectReportsReader WithHierarchy(params (Guid Manager, Guid Report)[] edges)
    {
        var tree = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var (manager, report) in edges)
        {
            if (!tree.TryGetValue(manager, out var reports))
                tree[manager] = reports = new();
            reports.Add(report);
        }

        return new FakeDirectReportsReader(tree);
    }

    public void Reparent(Guid employeeId, Guid newManagerId)
    {
        foreach (var reports in _tree.Values)
            reports.Remove(employeeId);

        if (!_tree.TryGetValue(newManagerId, out var newReports))
            _tree[newManagerId] = newReports = new();
        newReports.Add(employeeId);
    }

    public Task<IReadOnlyList<Guid>> GetDirectReportIdsAsync(
        Guid companyId, Guid managerId, CancellationToken cancellationToken) =>
        Task.FromResult(_flat ?? DirectReports(managerId));

    public Task<IReadOnlyList<Guid>> GetAllDescendantIdsAsync(
        Guid companyId, Guid managerId, CancellationToken cancellationToken) =>
        Task.FromResult(_flat ?? Descendants(managerId));

    private IReadOnlyList<Guid> DirectReports(Guid managerId) =>
        _tree.TryGetValue(managerId, out var reports) ? reports.ToList() : [];

    private IReadOnlyList<Guid> Descendants(Guid managerId)
    {
        var visited = new HashSet<Guid> { managerId };
        var result = new List<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(managerId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!_tree.TryGetValue(current, out var reports))
                continue;

            foreach (var report in reports)
            {
                if (visited.Add(report))
                {
                    result.Add(report);
                    queue.Enqueue(report);
                }
            }
        }

        return result;
    }
}
