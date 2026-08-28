using System.Text.Json;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Probation.Features.GetProbationRecordAuditHistory;

/// <summary>
/// AUD-07: entity activity history for a specific probation record.
/// Accessible to HR Administrators and Probation Managers (probation:manage policy).
/// </summary>
internal sealed class GetProbationRecordAuditHistoryHandler(
    IAuditHistoryReader auditHistoryReader,
    IEmployeeNameReader employeeNameReader)
{
    private const string EntityType = "ProbationRecord";

    public async Task<Result<GetProbationRecordAuditHistoryResponse>> HandleAsync(
        Guid companyId,
        Guid probationRecordId,
        CancellationToken cancellationToken)
    {
        var entries = await auditHistoryReader.GetEntityAuditHistoryAsync(
            companyId, EntityType, probationRecordId, cancellationToken);

        var actorEmployeeIds = entries
            .Where(e => e.ActorEmployeeId.HasValue)
            .Select(e => e.ActorEmployeeId!.Value)
            .Distinct()
            .ToList();

        var names = await employeeNameReader.GetNamesAsync(companyId, actorEmployeeIds, cancellationToken);

        var items = entries
            .Select(e => new ProbationAuditHistoryItem(
                e.OccurredAt,
                string.IsNullOrEmpty(e.Summary) ? e.EventType : e.Summary,
                ResolveUser(e.ActorEmployeeId, names),
                BuildChanges(ParseFields(e.BeforeJson), ParseFields(e.AfterJson))))
            .ToList();

        return Result.Success(new GetProbationRecordAuditHistoryResponse(items));
    }

    private static string ResolveUser(Guid? actorEmployeeId, IReadOnlyDictionary<Guid, string> names)
    {
        if (!actorEmployeeId.HasValue) return "System";
        return names.TryGetValue(actorEmployeeId.Value, out var name) ? name : "Unknown";
    }

    private static Dictionary<string, JsonElement> ParseFields(string? json) =>
        string.IsNullOrEmpty(json)
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];

    private static IReadOnlyList<ProbationAuditFieldChangeItem> BuildChanges(
        Dictionary<string, JsonElement> before,
        Dictionary<string, JsonElement> after)
    {
        var keys = before.Keys.Concat(after.Keys).Distinct(StringComparer.Ordinal).OrderBy(k => k);
        return keys
            .Select(k => new ProbationAuditFieldChangeItem(k,
                before.TryGetValue(k, out var b) ? b.ToString() : "—",
                after.TryGetValue(k, out var a) ? a.ToString() : "—"))
            .ToList();
    }
}
