using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Companies.Features.GetCompanyAuditLog;

/// <summary>
/// AUD-05: tenant-scoped audit search for HR Administrators.
/// Returns all audit events recorded against the company, with optional filtering by
/// employee, event type, and date range. The companyId isolation is enforced at the
/// IAuditHistoryReader.GetCompanyAuditLogAsync level — results are always bounded to a
/// single tenant.
/// Actor user IDs are resolved to display names (email) via IUserEmailDirectoryReader so
/// the caller never has to make a second round-trip.
/// </summary>
internal sealed class GetCompanyAuditLogHandler(
    IAuditHistoryReader auditHistoryReader,
    IUserEmailDirectoryReader userEmailDirectoryReader)
{
    public async Task<Result<GetCompanyAuditLogResponse>> HandleAsync(
        GetCompanyAuditLogRequest request,
        CancellationToken cancellationToken)
    {
        var pagination = new Pagination(request.PageNumber, request.PageSize);

        var page = await auditHistoryReader.GetCompanyAuditLogAsync(
            request.CompanyId,
            request.EmployeeId,
            request.FromDate,
            request.ToDate,
            request.EventType,
            pagination,
            cancellationToken);

        // Batch-resolve actor emails so the response carries display-ready actor names.
        var actorUserIds = page.Items
            .Where(e => e.ActorUserId.HasValue)
            .Select(e => e.ActorUserId!.Value)
            .Distinct()
            .ToList();

        var emailsByActorId = actorUserIds.Count > 0
            ? await userEmailDirectoryReader.GetEmailsByUserIdsAsync(actorUserIds, cancellationToken)
            : new Dictionary<Guid, string>();

        var items = page.Items
            .Select(e => new CompanyAuditLogItem(
                e.OccurredAt,
                e.EventType,
                e.EntityType,
                e.EntityId,
                e.EmployeeId,
                e.ActorUserId,
                e.ActorUserId.HasValue && emailsByActorId.TryGetValue(e.ActorUserId.Value, out var email)
                    ? email
                    : null,
                e.Summary))
            .ToList();

        return Result.Success(new GetCompanyAuditLogResponse(
            items, page.TotalCount, page.PageNumber, page.PageSize, page.TotalPages));
    }
}
