using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Companies.Features.GetCompanySettingsHistory;

internal sealed class GetCompanySettingsHistoryHandler(
    IAuditHistoryReader auditHistoryReader,
    IUserEmailDirectoryReader userEmailDirectoryReader)
{
    public const string EventType = "company-settings.updated";

    public async Task<Result<GetCompanySettingsHistoryResponse>> HandleAsync(
        GetCompanySettingsHistoryRequest request,
        CancellationToken cancellationToken)
    {
        var pagination = new Pagination(request.PageNumber, request.PageSize);

        var page = await auditHistoryReader.GetPlatformAuditLogAsync(
            request.CompanyId,
            actorUserIds: null,
            fromDate: null,
            toDate: null,
            eventType: EventType,
            pagination,
            cancellationToken);

        var actorIds = page.Items
            .Where(e => e.ActorUserId.HasValue)
            .Select(e => e.ActorUserId!.Value)
            .Distinct()
            .ToList();

        var emailsByActorId = await userEmailDirectoryReader.GetEmailsByUserIdsAsync(actorIds, cancellationToken);

        var items = page.Items
            .Select(e => new CompanySettingsHistoryItem(
                e.OccurredAt,
                "company-settings",
                e.ActorUserId,
                e.ActorUserId.HasValue && emailsByActorId.TryGetValue(e.ActorUserId.Value, out var email) ? email : null,
                e.BeforeJson,
                e.AfterJson))
            .ToList();

        return Result.Success(new GetCompanySettingsHistoryResponse(
            items, page.TotalCount, page.PageNumber, page.PageSize, page.TotalPages));
    }
}
