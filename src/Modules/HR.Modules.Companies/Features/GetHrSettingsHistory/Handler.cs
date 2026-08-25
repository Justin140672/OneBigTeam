using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Companies.Features.GetHrSettingsHistory;

/// <summary>
/// SET-02 counterpart to GetCompanySettingsHistory, scoped to HR Administrators and restricted to
/// the "hr-settings.updated" event type so it never surfaces company-profile (time zone/locale)
/// changes owned by the Company Administrator area.
/// </summary>
internal sealed class GetHrSettingsHistoryHandler(
    IAuditHistoryReader auditHistoryReader,
    IUserEmailDirectoryReader userEmailDirectoryReader)
{
    public const string EventType = "hr-settings.updated";

    public async Task<Result<GetHrSettingsHistoryResponse>> HandleAsync(
        GetHrSettingsHistoryRequest request,
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
            .Select(e => new HrSettingsHistoryItem(
                e.OccurredAt,
                "hr-settings",
                e.ActorUserId,
                e.ActorUserId.HasValue && emailsByActorId.TryGetValue(e.ActorUserId.Value, out var email) ? email : null,
                e.BeforeJson,
                e.AfterJson))
            .ToList();

        return Result.Success(new GetHrSettingsHistoryResponse(
            items, page.TotalCount, page.PageNumber, page.PageSize, page.TotalPages));
    }
}
