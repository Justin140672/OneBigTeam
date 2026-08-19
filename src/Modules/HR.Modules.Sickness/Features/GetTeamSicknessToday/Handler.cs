using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.GetTeamSicknessToday;

internal sealed class GetTeamSicknessTodayHandler(
    SicknessDbContext dbContext,
    IDirectReportsReader directReportsReader)
{
    public async Task<GetTeamSicknessTodayResponse> HandleAsync(
        GetTeamSicknessTodayRequest request,
        CancellationToken cancellationToken)
    {
        var directReportIds = await directReportsReader.GetDirectReportIdsAsync(
            request.CompanyId, request.ManagerId, cancellationToken);

        if (directReportIds.Count == 0)
            return new GetTeamSicknessTodayResponse([]);

        var items = await dbContext.SicknessRecords
            .AsNoTracking()
            .Where(r => r.CompanyId == request.CompanyId
                     && r.Status == SicknessStatus.Active
                     && directReportIds.Contains(r.EmployeeId))
            .OrderBy(r => r.StartDate)
            .Select(r => new TeamSicknessTodayItem(
                r.Id,
                r.EmployeeId,
                r.CategoryId,
                r.StartDate,
                r.EvidenceStatus.ToString()))
            .ToListAsync(cancellationToken);

        return new GetTeamSicknessTodayResponse(items);
    }
}
