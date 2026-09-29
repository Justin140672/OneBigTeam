using HR.Web.Models;

namespace HR.Web.Services;

public sealed class OrganisationHierarchyBuilder
{
    public IReadOnlyList<OrganisationChartNode> Build(IReadOnlyList<OrganisationChartEmployeeModel> employees)
    {
        var byId = employees.ToDictionary(e => e.EmployeeId);

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
