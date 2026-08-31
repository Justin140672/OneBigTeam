using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Tasks.Tests.Infrastructure;

/// <summary>
/// Fake <see cref="IDirectReportsReader"/> with two modes.
///
/// <para><b>Flat mode</b> (<c>new FakeDirectReportsReader(a, b, c)</c>): every manager id resolves
/// to exactly the given set for both the direct-reports and full-descendant queries. Call-sites
/// that model an "indirect report" simply by listing it here keep working unchanged.</para>
///
/// <para><b>Hierarchy mode</b> (<see cref="WithHierarchy"/>): builds a real
/// <c>(manager -&gt; direct reports)</c> adjacency map. <see cref="GetAllDescendantIdsAsync"/> walks
/// it with a breadth-first traversal guarded by a visited-set, so a reporting cycle
/// (A -&gt; B -&gt; A) or a self-referential manager terminates and no id is yielded twice. The map
/// is read fresh on every call, so <see cref="Reparent"/> moves an employee between managers'
/// sub-trees immediately, with nothing to invalidate. See
/// specifications/architecture/11-manager-hierarchy-scope.md (DSH-02).</para>
/// </summary>
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

    /// <summary>Moves <paramref name="employeeId"/> so their only manager is now
    /// <paramref name="newManagerId"/>. Hierarchy mode only.</summary>
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
