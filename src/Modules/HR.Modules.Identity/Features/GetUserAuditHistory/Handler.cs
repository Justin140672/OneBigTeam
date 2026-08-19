using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Identity.Features.GetUserAuditHistory;

// Identity-specific view of the same audit trail that also feeds the Employee Audit History tab
// (see HR.Modules.Employees.Features.GetEmployeeAuditHistory, which uses the same
// IAuditHistoryReader.GetEmployeeAuditHistoryAsync port and maps ApplicationUser/UserInvite entity
// types to the "Identity" module). This view is filtered to just the user-administration events.
internal sealed class GetUserAuditHistoryHandler(
    IAuditHistoryReader auditHistoryReader,
    IEmployeeNameReader employeeNameReader)
{
    private static readonly HashSet<string> IdentityEntityTypes = ["ApplicationUser", "UserInvite"];

    public async Task<Result<GetUserAuditHistoryResponse>> HandleAsync(
        GetUserAuditHistoryRequest request,
        CancellationToken cancellationToken)
    {
        var entries = await auditHistoryReader.GetEmployeeAuditHistoryAsync(
            request.CompanyId, request.EmployeeId, cancellationToken);

        var identityEntries = entries.Where(e => IdentityEntityTypes.Contains(e.EntityType)).ToList();

        var actorIds = identityEntries
            .Where(e => e.ActorUserId.HasValue)
            .Select(e => e.ActorUserId!.Value)
            .Distinct()
            .ToList();

        var names = await employeeNameReader.GetNamesAsync(request.CompanyId, actorIds, cancellationToken);

        var items = identityEntries
            .OrderByDescending(e => e.OccurredAt)
            .Select(e => new UserAuditHistoryItem(
                e.OccurredAt,
                e.EventType,
                string.IsNullOrEmpty(e.Summary) ? e.EventType : e.Summary,
                e.ActorUserId.HasValue
                    ? names.GetValueOrDefault(e.ActorUserId.Value, "Unknown")
                    : "System"))
            .ToList();

        return Result.Success(new GetUserAuditHistoryResponse(items));
    }
}
