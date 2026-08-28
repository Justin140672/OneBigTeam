using System.Text.Json;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Leave.Features.GetLeaveRequestAuditHistory;

/// <summary>
/// AUD-07: entity activity history for a specific leave request.
/// Follows the same pattern as GetSharedCompanyDocumentAuditHistory in the Documents module.
/// Accessible to HR Administrators (employee:manage policy).
/// </summary>
internal sealed class GetLeaveRequestAuditHistoryHandler(
    IAuditHistoryReader auditHistoryReader,
    IEmployeeNameReader employeeNameReader)
{
    private const string EntityType = "LeaveRequest";

    public async Task<Result<GetLeaveRequestAuditHistoryResponse>> HandleAsync(
        Guid companyId,
        Guid leaveRequestId,
        CancellationToken cancellationToken)
    {
        var entries = await auditHistoryReader.GetEntityAuditHistoryAsync(
            companyId, EntityType, leaveRequestId, cancellationToken);

        var actorEmployeeIds = entries
            .Where(e => e.ActorEmployeeId.HasValue)
            .Select(e => e.ActorEmployeeId!.Value)
            .Distinct()
            .ToList();

        var names = await employeeNameReader.GetNamesAsync(companyId, actorEmployeeIds, cancellationToken);

        var items = entries
            .Select(e => new LeaveAuditHistoryItem(
                e.OccurredAt,
                string.IsNullOrEmpty(e.Summary) ? e.EventType : e.Summary,
                ResolveUser(e.ActorEmployeeId, names),
                BuildChanges(ParseFields(e.BeforeJson), ParseFields(e.AfterJson))))
            .ToList();

        return Result.Success(new GetLeaveRequestAuditHistoryResponse(items));
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

    private static IReadOnlyList<LeaveAuditFieldChangeItem> BuildChanges(
        Dictionary<string, JsonElement> before,
        Dictionary<string, JsonElement> after)
    {
        var keys = before.Keys.Concat(after.Keys).Distinct(StringComparer.Ordinal).OrderBy(k => k);
        return keys
            .Select(k => new LeaveAuditFieldChangeItem(k,
                before.TryGetValue(k, out var b) ? b.ToString() : "—",
                after.TryGetValue(k, out var a) ? a.ToString() : "—"))
            .ToList();
    }
}
