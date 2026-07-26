using System.Text.Json;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetEmployeeAuditHistory;

internal sealed class GetEmployeeAuditHistoryHandler(
    IAuditHistoryReader auditHistoryReader,
    IEmployeeNameReader employeeNameReader,
    EmployeesDbContext dbContext)
{
    // Snapshot fields that carry a raw foreign-key Guid rather than a human-readable value.
    // Resolved to display names below so the Before/After table never leaks a bare ID.
    private const string DepartmentIdField = "DepartmentId";
    private const string PositionProfileIdField = "PositionProfileId";
    private const string LocationIdField = "LocationId";

    // Compensation's Reason snapshot field carries a PascalCase enum value (e.g. "AnnualReview")
    // rather than a human-readable one — reuse the same Humanize() already applied to field names
    // below rather than hardcoding a separate value-to-label table.
    private const string ReasonField = "Reason";

    private static readonly IReadOnlyDictionary<string, string> ModuleMap = new Dictionary<string, string>
    {
        ["Employee"] = "Employees",
        ["Compensation"] = "Employees",
        ["EmployeeLeavingProcess"] = "Employees",
        ["EmployeePromotion"] = "Employees",
        ["SicknessRecord"] = "Sickness",
        ["SicknessEvidenceRequest"] = "Sickness",
        ["ReturnToWorkReview"] = "Sickness",
        ["LeaveRequest"] = "Leave",
        ["LeaveBalance"] = "Leave",
        ["ToilTransaction"] = "Leave",
        ["ProbationRecord"] = "Probation",
        ["ProbationReview"] = "Probation",
        ["OnboardingPlan"] = "Onboarding",
        ["OffboardingPlan"] = "Offboarding",
        ["EmployeeDocument"] = "Documents",
        ["DocumentRequest"] = "Documents",
        ["AssetAssignment"] = "Assets",
        ["Candidate"] = "Recruitment",
    };

    public async Task<Result<GetEmployeeAuditHistoryResponse>> HandleAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var entries = await auditHistoryReader.GetEmployeeAuditHistoryAsync(companyId, employeeId, cancellationToken);

        var actorEmployeeIds = entries
            .Where(e => e.ActorEmployeeId.HasValue)
            .Select(e => e.ActorEmployeeId!.Value)
            .Distinct()
            .ToList();

        var names = await employeeNameReader.GetNamesAsync(companyId, actorEmployeeIds, cancellationToken);

        // Parse each entry's Before/After JSON once up front so we can (a) collect every
        // DepartmentId/PositionProfileId/LocationId referenced anywhere in the history and
        // resolve them to display names in a single batched query per entity type, then
        // (b) reuse the already-parsed dictionaries to build the change rows below.
        var parsed = entries
            .Select(e => (Entry: e, Before: ParseFields(e.BeforeJson), After: ParseFields(e.AfterJson)))
            .ToList();

        var departmentIds = CollectReferencedIds(parsed, DepartmentIdField);
        var positionProfileIds = CollectReferencedIds(parsed, PositionProfileIdField);
        var locationIds = CollectReferencedIds(parsed, LocationIdField);

        var departmentNames = departmentIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.Departments
                .AsNoTracking()
                .Where(d => d.CompanyId == companyId && departmentIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);

        var positionProfileNames = positionProfileIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.PositionProfiles
                .AsNoTracking()
                .Where(p => p.CompanyId == companyId && positionProfileIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Title, cancellationToken);

        var locationNames = locationIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.Locations
                .AsNoTracking()
                .Where(l => l.CompanyId == companyId && locationIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken);

        var items = parsed
            .Select(p => new AuditHistoryItem(
                p.Entry.OccurredAt,
                string.IsNullOrEmpty(p.Entry.Summary) ? p.Entry.EventType : p.Entry.Summary,
                ModuleMap.TryGetValue(p.Entry.EntityType, out var module) ? module : p.Entry.EntityType,
                ResolveUser(p.Entry.ActorEmployeeId, names),
                BuildChanges(p.Before, p.After, departmentNames, positionProfileNames, locationNames)))
            .ToList();

        return Result.Success(new GetEmployeeAuditHistoryResponse(items));
    }

    private static string ResolveUser(Guid? actorEmployeeId, IReadOnlyDictionary<Guid, string> names)
    {
        if (!actorEmployeeId.HasValue)
            return "System";

        return names.TryGetValue(actorEmployeeId.Value, out var name) ? name : "Unknown";
    }

    private static HashSet<Guid> CollectReferencedIds(
        IEnumerable<(AuditHistoryEntry Entry, Dictionary<string, JsonElement> Before, Dictionary<string, JsonElement> After)> parsed,
        string fieldName)
    {
        var ids = new HashSet<Guid>();

        foreach (var p in parsed)
        {
            if (p.Before.TryGetValue(fieldName, out var b) && b.ValueKind == JsonValueKind.String && b.TryGetGuid(out var bg))
                ids.Add(bg);

            if (p.After.TryGetValue(fieldName, out var a) && a.ValueKind == JsonValueKind.String && a.TryGetGuid(out var ag))
                ids.Add(ag);
        }

        return ids;
    }

    private static IReadOnlyList<AuditFieldChangeItem> BuildChanges(
        Dictionary<string, JsonElement> before,
        Dictionary<string, JsonElement> after,
        IReadOnlyDictionary<Guid, string> departmentNames,
        IReadOnlyDictionary<Guid, string> positionProfileNames,
        IReadOnlyDictionary<Guid, string> locationNames)
    {
        var keys = before.Keys.Concat(after.Keys).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal);

        return keys
            .Select(key => new AuditFieldChangeItem(
                Humanize(key),
                before.TryGetValue(key, out var b) ? FormatValue(key, b, departmentNames, positionProfileNames, locationNames) : "—",
                after.TryGetValue(key, out var a) ? FormatValue(key, a, departmentNames, positionProfileNames, locationNames) : "—"))
            .ToList();
    }

    private static Dictionary<string, JsonElement> ParseFields(string? json) =>
        string.IsNullOrEmpty(json)
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];

    private static string FormatValue(
        string fieldName,
        JsonElement element,
        IReadOnlyDictionary<Guid, string> departmentNames,
        IReadOnlyDictionary<Guid, string> positionProfileNames,
        IReadOnlyDictionary<Guid, string> locationNames)
    {
        if (element.ValueKind == JsonValueKind.Null)
            return "—";

        if (element.ValueKind == JsonValueKind.String && element.TryGetGuid(out var id))
        {
            var lookup = fieldName switch
            {
                DepartmentIdField => departmentNames,
                PositionProfileIdField => positionProfileNames,
                LocationIdField => locationNames,
                _ => null,
            };

            if (lookup is not null)
                return lookup.TryGetValue(id, out var name) ? name : "Unknown";
        }

        if (fieldName == ReasonField && element.ValueKind == JsonValueKind.String)
            return Humanize(element.GetString()!);

        return element.ToString();
    }

    private static string Humanize(string fieldName) =>
        System.Text.RegularExpressions.Regex.Replace(fieldName, "(?<!^)([A-Z])", " $1");
}
