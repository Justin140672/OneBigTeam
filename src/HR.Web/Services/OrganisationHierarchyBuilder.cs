using HR.Web.Models;

namespace HR.Web.Services;

/// <summary>
/// Converts a flat list of employees (each carrying its own ManagerId) into a tree of
/// <see cref="OrganisationChartNode"/> for rendering an organisation chart.
/// </summary>
public sealed class OrganisationHierarchyBuilder
{
    public IReadOnlyList<OrganisationChartNode> Build(IReadOnlyList<OrganisationChartEmployeeModel> employees)
    {
        var byId = employees.ToDictionary(e => e.EmployeeId);

        // A manager pointer that doesn't resolve to another employee in this set (no manager, or
        // the manager isn't part of the chart — e.g. inactive) is treated the same as "no manager":
        // the employee becomes a root node rather than being dropped.
        var childrenByManagerId = employees
            .Where(e => e.ManagerId.HasValue && byId.ContainsKey(e.ManagerId.Value))
            .GroupBy(e => e.ManagerId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList());

        var visited = new HashSet<Guid>();
        var roots = new List<OrganisationChartNode>();

        foreach (var employee in employees
                     .Where(e => e.ManagerId is null || !byId.ContainsKey(e.ManagerId.Value))
                     .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            roots.Add(BuildNode(employee, childrenByManagerId, visited, ancestors: []));
        }

        // Anything still unvisited only has managers within a cycle (A -> B -> A, or longer), so it
        // was never reachable from a genuine root. Promote the first such employee encountered in
        // each cycle to a root of its own — this breaks the cycle for display purposes while still
        // showing every employee, rather than silently dropping them.
        foreach (var employee in employees.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!visited.Contains(employee.EmployeeId))
                roots.Add(BuildNode(employee, childrenByManagerId, visited, ancestors: []));
        }

        return roots;
    }

    private static OrganisationChartNode BuildNode(
        OrganisationChartEmployeeModel employee,
        IReadOnlyDictionary<Guid, List<OrganisationChartEmployeeModel>> childrenByManagerId,
        HashSet<Guid> visited,
        HashSet<Guid> ancestors)
    {
        visited.Add(employee.EmployeeId);

        var directReports = new List<OrganisationChartNode>();

        if (childrenByManagerId.TryGetValue(employee.EmployeeId, out var reports))
        {
            var childAncestors = new HashSet<Guid>(ancestors) { employee.EmployeeId };

            foreach (var report in reports)
            {
                // The reporting chain loops back on itself (e.g. this employee is, transitively —
                // or even directly — their own manager) — stop descending here rather than
                // recursing forever. childAncestors includes this employee, so a direct self-loop
                // (report.EmployeeId == employee.EmployeeId) is caught immediately, not just on the
                // next level down.
                if (childAncestors.Contains(report.EmployeeId))
                    continue;

                directReports.Add(BuildNode(report, childrenByManagerId, visited, childAncestors));
            }
        }

        return new OrganisationChartNode(
            employee.EmployeeId,
            employee.Name,
            employee.JobTitle,
            employee.Department,
            employee.Location,
            employee.ProfilePhotoUrl,
            directReports);
    }
}
