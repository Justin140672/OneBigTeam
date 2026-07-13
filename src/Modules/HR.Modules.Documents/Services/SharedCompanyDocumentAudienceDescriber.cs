using HR.Infrastructure.Abstractions;

namespace HR.Modules.Documents.Services;

/// <summary>
/// Builds the human-readable "who can see this" summary shown on the HR detail screen and
/// returned by the audience-update endpoint — shared so both always describe a rule set the same
/// way.
/// </summary>
internal sealed class SharedCompanyDocumentAudienceDescriber(
    IEmployeeAudienceReader audienceReader,
    IEmployeeNameReader employeeNameReader)
{
    public async Task<string> DescribeAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> departmentIds,
        IReadOnlyCollection<Guid> locationIds,
        IReadOnlyCollection<Guid> positionProfileIds,
        IReadOnlyCollection<Guid> employeeIds,
        CancellationToken cancellationToken)
    {
        if (departmentIds.Count == 0 && locationIds.Count == 0 && positionProfileIds.Count == 0 && employeeIds.Count == 0)
            return "All Employees";

        var parts = new List<string>();

        if (departmentIds.Count > 0)
        {
            var names = new List<string>();
            foreach (var id in departmentIds)
                names.Add(await audienceReader.GetDepartmentNameAsync(companyId, id, cancellationToken) ?? "Unknown");
            parts.Add($"Departments: {string.Join(", ", names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))}");
        }

        if (locationIds.Count > 0)
        {
            var names = new List<string>();
            foreach (var id in locationIds)
                names.Add(await audienceReader.GetLocationNameAsync(companyId, id, cancellationToken) ?? "Unknown");
            parts.Add($"Locations: {string.Join(", ", names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))}");
        }

        if (positionProfileIds.Count > 0)
        {
            var names = new List<string>();
            foreach (var id in positionProfileIds)
                names.Add(await audienceReader.GetPositionProfileNameAsync(companyId, id, cancellationToken) ?? "Unknown");
            parts.Add($"Positions: {string.Join(", ", names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))}");
        }

        if (employeeIds.Count > 0)
        {
            var namesLookup = await employeeNameReader.GetNamesAsync(companyId, employeeIds, cancellationToken);
            var names = employeeIds.Select(id => namesLookup.TryGetValue(id, out var n) ? n : "Unknown");
            parts.Add($"Employees: {string.Join(", ", names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))}");
        }

        return string.Join("; ", parts);
    }
}
