using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Features.GetAdministrativeAlerts;

internal sealed class GetAdministrativeAlertsHandler(NotificationsDbContext dbContext)
{
    public async Task<GetAdministrativeAlertsResponse> HandleAsync(
        GetAdministrativeAlertsRequest request,
        CancellationToken cancellationToken)
    {
        // ADM-03: unread badge count is an independent query — unfiltered/unpaginated, and always
        // excludes resolved alerts regardless of the caller's Status/IsRead filters below.
        var unreadCount = await dbContext.AdministrativeAlerts
            .AsNoTracking()
            .CountAsync(
                a => a.CompanyId == request.CompanyId
                     && !a.IsRead
                     && a.Status != AdministrativeAlertStatus.Resolved,
                cancellationToken);

        var query = dbContext.AdministrativeAlerts
            .AsNoTracking()
            .Where(a => a.CompanyId == request.CompanyId);

        if (request.Severity is not null)
            query = query.Where(a => a.Severity == request.Severity.Value);

        if (request.Category is not null)
            query = query.Where(a => a.Category == request.Category.Value);

        if (request.Status is not null)
            query = query.Where(a => a.Status == request.Status.Value);

        if (request.IsRead is not null)
            query = query.Where(a => a.IsRead == request.IsRead.Value);

        if (request.OccurredFrom is not null)
            query = query.Where(a => a.LastOccurredAt >= request.OccurredFrom.Value);

        if (request.OccurredTo is not null)
            query = query.Where(a => a.LastOccurredAt <= request.OccurredTo.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var pageSize = request.PageSize <= 0 ? 50 : request.PageSize;
        var pageNumber = request.PageNumber <= 0 ? 1 : request.PageNumber;

        var items = await query
            .OrderByDescending(a => a.Status == AdministrativeAlertStatus.Open)
            .ThenByDescending(a => a.Severity)
            .ThenByDescending(a => a.LastOccurredAt)
            .ThenByDescending(a => a.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AdministrativeAlertItem(
                a.Id,
                a.Severity.ToString(),
                a.Category.ToString(),
                a.Summary,
                a.Detail,
                a.OccurrenceCount,
                a.FirstOccurredAt,
                a.LastOccurredAt,
                a.AffectedEntityType,
                a.AffectedEntityId,
                a.RecommendedAction,
                a.ActionUrl,
                a.IsRead,
                a.Status.ToString(),
                a.AcknowledgedAt,
                a.ResolvedAt,
                a.ResolutionNote))
            .ToListAsync(cancellationToken);

        var totalPages = pageSize == 0 ? 0 : (int)Math.Ceiling((double)totalCount / pageSize);

        return new GetAdministrativeAlertsResponse(unreadCount, items, totalCount, pageNumber, pageSize, totalPages);
    }
}
