using System.Text.Json;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Documents.Features.GetSharedCompanyDocumentAuditHistory;

internal sealed class GetSharedCompanyDocumentAuditHistoryHandler(
    IAuditHistoryReader auditHistoryReader,
    IEmployeeNameReader employeeNameReader)
{
    private const string EntityType = "SharedCompanyDocument";

    public async Task<Result<GetSharedCompanyDocumentAuditHistoryResponse>> HandleAsync(
        Guid companyId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var entries = await auditHistoryReader.GetEntityAuditHistoryAsync(
            companyId, EntityType, documentId, cancellationToken);

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
                ResolveUser(e.ActorEmployeeId, names),
                BuildChanges(ParseFields(e.BeforeJson), ParseFields(e.AfterJson))))
            .ToList();

        return Result.Success(new GetSharedCompanyDocumentAuditHistoryResponse(items));
    }

    private static string ResolveUser(Guid? actorEmployeeId, IReadOnlyDictionary<Guid, string> names)
    {
        if (!actorEmployeeId.HasValue)
            return "System";

        return names.TryGetValue(actorEmployeeId.Value, out var name) ? name : "Unknown";
    }

    private static Dictionary<string, JsonElement> ParseFields(string? json) =>
        string.IsNullOrEmpty(json)
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];

    private static IReadOnlyList<AuditFieldChangeItem> BuildChanges(
        Dictionary<string, JsonElement> before,
        Dictionary<string, JsonElement> after)
    {
        var keys = before.Keys.Concat(after.Keys).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal);

        return keys
            .Select(key => new AuditFieldChangeItem(
                Humanize(key),
                before.TryGetValue(key, out var b) ? FormatValue(b) : "—",
                after.TryGetValue(key, out var a) ? FormatValue(a) : "—"))
            .ToList();
    }

    private static string FormatValue(JsonElement element) =>
        element.ValueKind == JsonValueKind.Null ? "—" : element.ToString();

    private static string Humanize(string fieldName) =>
        System.Text.RegularExpressions.Regex.Replace(fieldName, "(?<!^)([A-Z])", " $1");
}
