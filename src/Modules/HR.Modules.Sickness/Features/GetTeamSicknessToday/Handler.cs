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
        // DSH-02: dashboard "my team" = the manager's entire reporting sub-tree (direct and
        // indirect reports). See specifications/architecture/11-manager-hierarchy-scope.md.
        var teamIds = await directReportsReader.GetAllDescendantIdsAsync(
            request.CompanyId, request.ManagerId, cancellationToken);

        if (teamIds.Count == 0)
            return new GetTeamSicknessTodayResponse([]);

        var items = await dbContext.SicknessRecords
            .AsNoTracking()
            .Where(r => r.CompanyId == request.CompanyId
                     && r.Status == SicknessStatus.Active
                     && teamIds.Contains(r.EmployeeId))
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
