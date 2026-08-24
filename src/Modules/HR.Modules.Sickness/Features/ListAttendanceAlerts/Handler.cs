using HR.Modules.Sickness.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.ListAttendanceAlerts;

internal sealed class ListAttendanceAlertsHandler(SicknessDbContext dbContext)
{
    public async Task<ListAttendanceAlertsResponse> HandleAsync(
        ListAttendanceAlertsRequest request,
        IReadOnlySet<Guid>? authorizedEmployeeIds,
        bool isHrAdministrator,
        CancellationToken cancellationToken)
    {
        // authorizedEmployeeIds is null for HR Administrators (company-wide, unrestricted). For
        // managers it is their full reporting hierarchy — resolved server-side by the endpoint via
        // SicknessResourceAuthorizer, never trusted from the client (mirrors SICK-02).
        if (authorizedEmployeeIds is not null && authorizedEmployeeIds.Count == 0)
            return new ListAttendanceAlertsResponse([]);

        var rows = await dbContext.AttendanceAlerts
            .AsNoTracking()
            .Where(a => a.CompanyId == request.CompanyId
                     && (authorizedEmployeeIds == null || authorizedEmployeeIds.Contains(a.EmployeeId)))
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new
            {
                a.Id,
                a.EmployeeId,
                a.Rule,
                a.OccurrenceCount,
                a.EvidencePeriodStart,
                a.EvidencePeriodEnd,
                a.Description,
                a.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(a => new AttendanceAlertItem(
                a.Id,
                a.EmployeeId,
                a.Rule.ToString(),
                a.OccurrenceCount,
                isHrAdministrator ? a.EvidencePeriodStart : null,
                isHrAdministrator ? a.EvidencePeriodEnd : null,
                isHrAdministrator ? a.Description : null,
                a.CreatedAt))
            .ToList();

        return new ListAttendanceAlertsResponse(items);
    }
}
