using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Employees.Features.GetRecentEmployeeChanges;

internal sealed class GetRecentEmployeeChangesHandler(
    IAuditHistoryReader auditHistoryReader,
    IEmployeeNameReader employeeNameReader)
{
    // "Employee" is the audit EntityType recorded by the Employees module for
    // EmployeeCreated/EmployeeUpdated/EmployeeTerminated (see Employee module's audit writes) —
    // matches the "Employees" grouping used by GetEmployeeAuditHistoryHandler.ModuleMap.
    private static readonly string[] EmployeeEntityTypes = ["Employee"];

    private const int MaxItems = 15;

    public async Task<GetRecentEmployeeChangesResponse> HandleAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var entries = await auditHistoryReader.GetRecentCompanyAuditHistoryAsync(
            companyId, EmployeeEntityTypes, MaxItems, cancellationToken);

        var employeeIds = entries.Where(e => e.EmployeeId.HasValue).Select(e => e.EmployeeId!.Value);
        var actorIds = entries.Where(e => e.ActorEmployeeId.HasValue).Select(e => e.ActorEmployeeId!.Value);
        var names = await employeeNameReader.GetNamesAsync(companyId, employeeIds.Concat(actorIds).Distinct(), cancellationToken);

        var items = entries
            .Select(e => new RecentEmployeeChangeItem(
                e.OccurredAt,
                ResolveName(e.EmployeeId, names),
                string.IsNullOrEmpty(e.Summary) ? Humanize(e.EventType) : e.Summary,
                e.ActorEmployeeId.HasValue ? ResolveName(e.ActorEmployeeId, names) : "System"))
            .ToList();

        return new GetRecentEmployeeChangesResponse(items);
    }

    private static string ResolveName(Guid? employeeId, IReadOnlyDictionary<Guid, string> names) =>
        employeeId.HasValue && names.TryGetValue(employeeId.Value, out var name) ? name : "Unknown";

    private static string Humanize(string eventType) =>
        System.Text.RegularExpressions.Regex.Replace(eventType, "(?<!^)([A-Z])", " $1");
}
