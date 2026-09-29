using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Companies.Features.GetCompanyAuditLog;

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
