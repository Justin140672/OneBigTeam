using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Persistence;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Features.ListOperationalAlerts;

/// <summary>
/// Follow-up B: platform-admin Operational Alerts list. Relies solely on the global
/// <c>platform:admin</c> policy (same modern pattern as GetAuditLog) — no tenant scoping.
/// </summary>
internal sealed class ListOperationalAlertsHandler(NotificationsDbContext dbContext)
{
    public async Task<Result<ListOperationalAlertsResponse>> HandleAsync(
        ListOperationalAlertsRequest request,
        CancellationToken cancellationToken)
    {
        var query = dbContext.AdministrativeAlerts.AsNoTracking();

        if (request.CompanyId is { } companyId)
            query = query.Where(a => a.CompanyId == companyId);

        if (request.Category is not null
            && Enum.TryParse<AdministrativeAlertCategory>(request.Category, ignoreCase: true, out var category))
        {
            query = query.Where(a => a.Category == category);
        }

        if (request.Status == "open")
            query = query.Where(a => a.Status != AdministrativeAlertStatus.Resolved);
        else if (request.Status == "resolved")
            query = query.Where(a => a.Status == AdministrativeAlertStatus.Resolved);

        var totalCount = await query.CountAsync(cancellationToken);

        var alerts = await query
            .OrderByDescending(a => a.LastOccurredAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = alerts
            .Select(a => new OperationalAlertListItemDto(
                a.Id,
                a.CompanyId,
                a.Category.ToString(),
                a.Severity.ToString(),
                a.Status.ToString(),
                a.Summary,
                a.OccurrenceCount,
                a.FirstOccurredAt,
                a.LastOccurredAt,
                a.AffectedEntityType,
                a.AffectedEntityId,
                a.AffectedItemCount,
                a.ResolvedAt,
                a.ResolvedByUserId,
                a.IsRead))
            .ToList();

        return Result.Success(new ListOperationalAlertsResponse(
            items, totalCount, request.Page, request.PageSize));
    }
}
