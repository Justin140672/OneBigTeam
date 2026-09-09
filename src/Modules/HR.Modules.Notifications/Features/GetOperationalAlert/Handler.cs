using System.Threading;
using System.Threading.Tasks;

using HR.Modules.Notifications.Persistence;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Features.GetOperationalAlert;

internal sealed class GetOperationalAlertHandler(NotificationsDbContext dbContext)
{
    public async Task<Result<GetOperationalAlertResponse>> HandleAsync(
        GetOperationalAlertRequest request,
        CancellationToken cancellationToken)
    {
        var alert = await dbContext.AdministrativeAlerts
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == request.AlertId, cancellationToken);

        if (alert is null)
            return Result.Failure<GetOperationalAlertResponse>(
                Error.NotFound($"Operational alert '{request.AlertId}' was not found."));

        return Result.Success(new GetOperationalAlertResponse(
            alert.Id,
            alert.CompanyId,
            alert.Category.ToString(),
            alert.Severity.ToString(),
            alert.Status.ToString(),
            alert.Summary,
            alert.OccurrenceCount,
            alert.FirstOccurredAt,
            alert.LastOccurredAt,
            alert.AffectedEntityType,
            alert.AffectedEntityId,
            alert.AffectedItemCount,
            alert.ResolvedAt,
            alert.ResolvedByUserId,
            alert.IsRead,
            alert.Detail,
            alert.DedupKey,
            alert.RecommendedAction,
            alert.ActionUrl,
            alert.CreatedAt,
            alert.AcknowledgedAt,
            alert.AcknowledgedByUserId,
            alert.ResolutionNote));
    }
}
