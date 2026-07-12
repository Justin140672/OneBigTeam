using System.Text.Json;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Employees.Features.GetEmployeeAuditHistory;

internal sealed class GetEmployeeAuditHistoryHandler(
    IAuditHistoryReader auditHistoryReader,
    IEmployeeNameReader employeeNameReader)
{
    private static readonly IReadOnlyDictionary<string, string> ModuleMap = new Dictionary<string, string>
    {
        ["Employee"] = "Employees",
        ["Compensation"] = "Employees",
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

        var items = entries
            .Select(e => new AuditHistoryItem(
                e.OccurredAt,
                string.IsNullOrEmpty(e.Summary) ? e.EventType : e.Summary,
                ModuleMap.TryGetValue(e.EntityType, out var module) ? module : e.EntityType,
                ResolveUser(e.ActorEmployeeId, names),
                BuildChanges(e.BeforeJson, e.AfterJson)))
            .ToList();

        return Result.Success(new GetEmployeeAuditHistoryResponse(items));
    }

    private static string ResolveUser(Guid? actorEmployeeId, IReadOnlyDictionary<Guid, string> names)
    {
        if (!actorEmployeeId.HasValue)
            return "System";

        return names.TryGetValue(actorEmployeeId.Value, out var name) ? name : "Unknown";
    }

    private static IReadOnlyList<AuditFieldChangeItem> BuildChanges(string? beforeJson, string? afterJson)
    {
        var before = ParseFields(beforeJson);
        var after = ParseFields(afterJson);

        var keys = before.Keys.Concat(after.Keys).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal);

        return keys
            .Select(key => new AuditFieldChangeItem(
                Humanize(key),
                before.TryGetValue(key, out var b) ? FormatValue(b) : "—",
                after.TryGetValue(key, out var a) ? FormatValue(a) : "—"))
            .ToList();
    }

    private static Dictionary<string, JsonElement> ParseFields(string? json) =>
        string.IsNullOrEmpty(json)
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];

    private static string FormatValue(JsonElement element) =>
        element.ValueKind == JsonValueKind.Null ? "—" : element.ToString();

    private static string Humanize(string fieldName) =>
        System.Text.RegularExpressions.Regex.Replace(fieldName, "(?<!^)([A-Z])", " $1");
}
