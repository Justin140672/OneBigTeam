using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed record EmploymentTypeGroup(Guid EmploymentTypeId, string Label, bool IsNonCanonical, int EmployeeCount);

// Employment types are always grouped and filtered by EmploymentTypeId. The label comes from the
// configured record; inactive, unreviewed-import and missing records are flagged in the label so
// dashboards and reports never present a non-configured value as if it were canonical.
internal static class EmploymentTypeGrouping
{
    public const string UnknownLabel = "Unknown (legacy value)";
    public const string InactiveSuffix = " (inactive)";
    public const string UnreviewedSuffix = " (unreviewed import)";

    public static string Label(string name, bool isActive, bool requiresReview)
    {
        var label = name;
        if (!isActive)
            label += InactiveSuffix;
        if (requiresReview)
            label += UnreviewedSuffix;
        return label;
    }

    public static async Task<IReadOnlyDictionary<Guid, (string Label, bool IsNonCanonical)>> LoadLabelsAsync(
        EmployeesDbContext dbContext,
        Guid companyId,
        IReadOnlyCollection<Guid> employmentTypeIds,
        CancellationToken cancellationToken)
    {
        if (employmentTypeIds.Count == 0)
            return new Dictionary<Guid, (string, bool)>();

        var rows = await dbContext.EmploymentTypes
            .AsNoTracking()
            .Where(t => t.CompanyId == companyId && employmentTypeIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name, t.IsActive, t.RequiresReview })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            r => r.Id,
            r => (Label(r.Name, r.IsActive, r.RequiresReview), !r.IsActive || r.RequiresReview));
    }

    public static (string Label, bool IsNonCanonical) Resolve(
        IReadOnlyDictionary<Guid, (string Label, bool IsNonCanonical)> labels, Guid employmentTypeId) =>
        labels.TryGetValue(employmentTypeId, out var found) ? found : (UnknownLabel, true);

    public static IReadOnlyList<EmploymentTypeGroup> Group(
        IEnumerable<Guid> employeeEmploymentTypeIds,
        IReadOnlyDictionary<Guid, (string Label, bool IsNonCanonical)> labels) =>
        employeeEmploymentTypeIds
            .GroupBy(id => id)
            .Select(g =>
            {
                var (label, nonCanonical) = Resolve(labels, g.Key);
                return new EmploymentTypeGroup(g.Key, label, nonCanonical, g.Count());
            })
            .OrderBy(g => g.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
