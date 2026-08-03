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

    // ManagerId is resolved to "{FirstName} {LastName}" the same way Department/PositionProfile/
    // Location Guids are, via a batched lookup against Employees below — a raw Guid is never
    // useful to a reader of audit history. Null is a valid value ("No Manager") and is already
    // rendered as "—" by FormatValue's existing null handling, so no special casing is needed there.
    private const string ManagerIdField = "ManagerId";

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
        ["ApplicationUser"] = "Identity",
        ["UserInvite"] = "Identity",
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
        var managerIds = CollectReferencedIds(parsed, ManagerIdField);

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

        var managerNames = managerIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.Employees
                .AsNoTracking()
                .Where(e => e.CompanyId == companyId && managerIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => $"{e.FirstName} {e.LastName}", cancellationToken);

        var candidates = parsed
            .Select(p => (
                Entry: p.Entry,
                Item: new AuditHistoryItem(
                    p.Entry.OccurredAt,
                    string.IsNullOrEmpty(p.Entry.Summary) ? p.Entry.EventType : p.Entry.Summary,
                    ModuleMap.TryGetValue(p.Entry.EntityType, out var module) ? module : p.Entry.EntityType,
                    ResolveUser(p.Entry.ActorEmployeeId, names),
                    BuildChanges(p.Before, p.After, departmentNames, positionProfileNames, locationNames, managerNames))))
            .ToList();

        var items = MergeCorrelatedItems(candidates);

        return Result.Success(new GetEmployeeAuditHistoryResponse(items));
    }

    // Ticket: "merge Employee + Employment tab audit entries when saved together". Entries sharing
    // a non-null CorrelationId (set by EmployeeEdit.razor's combined Save action — see
    // UpdateEmployeeProfileRequest/UpdateEmploymentDetailsRequest.CorrelationId) are combined into
    // one AuditHistoryItem so the reader sees a single "Employee profile and employment details
    // updated" entry rather than two separate rows for what was really one save. Entries with a
    // null CorrelationId, or a CorrelationId not shared by any other entry, pass through unchanged.
    private static List<AuditHistoryItem> MergeCorrelatedItems(
        IReadOnlyList<(AuditHistoryEntry Entry, AuditHistoryItem Item)> candidates)
    {
        var correlationGroupSizes = candidates
            .Where(c => c.Entry.CorrelationId.HasValue)
            .GroupBy(c => c.Entry.CorrelationId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var result = new List<AuditHistoryItem>();
        var mergedCorrelationIds = new HashSet<Guid>();

        foreach (var candidate in candidates)
        {
            var correlationId = candidate.Entry.CorrelationId;

            if (!correlationId.HasValue || correlationGroupSizes[correlationId.Value] <= 1)
            {
                result.Add(candidate.Item);
                continue;
            }

            if (!mergedCorrelationIds.Add(correlationId.Value))
                continue; // Already merged and added when we encountered the first member of this group.

            var group = candidates.Where(c => c.Entry.CorrelationId == correlationId.Value).ToList();

            var mergedChanges = group
                .SelectMany(c => c.Item.Changes)
                .ToList();

            var eventTypes = group.Select(c => c.Entry.EventType).Distinct().ToList();
            var summary = eventTypes.Count > 1
                ? "Employee profile and employment details updated"
                : group[0].Item.Action;

            var earliest = group.OrderBy(c => c.Entry.OccurredAt).First();

            result.Add(new AuditHistoryItem(
                earliest.Entry.OccurredAt,
                summary,
                earliest.Item.Module,
                earliest.Item.User,
                mergedChanges));
        }

        return result.OrderByDescending(i => i.OccurredAt).ToList();
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
        IReadOnlyDictionary<Guid, string> locationNames,
        IReadOnlyDictionary<Guid, string> managerNames)
    {
        var keys = before.Keys.Concat(after.Keys).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal);

        return keys
            .Select(key => new AuditFieldChangeItem(
                Humanize(key),
                before.TryGetValue(key, out var b) ? FormatValue(key, b, departmentNames, positionProfileNames, locationNames, managerNames) : "—",
                after.TryGetValue(key, out var a) ? FormatValue(key, a, departmentNames, positionProfileNames, locationNames, managerNames) : "—"))
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
        IReadOnlyDictionary<Guid, string> locationNames,
        IReadOnlyDictionary<Guid, string> managerNames)
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
                ManagerIdField => managerNames,
                _ => null,
            };

            if (lookup is not null)
                return lookup.TryGetValue(id, out var name) ? name : "Unknown";
        }

        if (fieldName == ReasonField && element.ValueKind == JsonValueKind.String)
            return Humanize(element.GetString()!);

        return element.ToString();
    }

    // Fields carrying a raw foreign-key Guid are resolved to a display name (see FormatValue) so
    // their label should read as the plain entity name too, not "<Entity> Id" — Humanize alone
    // would otherwise leave the "Id" suffix in place (e.g. "Location Id").
    private static readonly IReadOnlyDictionary<string, string> FriendlyFieldLabels = new Dictionary<string, string>
    {
        [DepartmentIdField] = "Department",
        [PositionProfileIdField] = "Position",
        [LocationIdField] = "Location",
        [ManagerIdField] = "Manager",
    };

    private static string Humanize(string fieldName) =>
        FriendlyFieldLabels.TryGetValue(fieldName, out var friendly)
            ? friendly
            : System.Text.RegularExpressions.Regex.Replace(fieldName, "(?<!^)([A-Z])", " $1");
}
