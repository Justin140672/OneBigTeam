using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetMyTeam;

internal sealed class GetMyTeamHandler(EmployeesDbContext dbContext, IProfilePhotoReader profilePhotoReader)
{
    public async Task<GetMyTeamResponse> HandleAsync(
        Guid companyId, Guid managerId, bool includeIndirect, CancellationToken cancellationToken)
    {
        // Pulled flat and walked in memory via ManagerId links — same established pattern as
        // GetOrganisationChartHandler (no recursive-CTE pattern exists anywhere in this codebase).
        var employees = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.Status == EmploymentStatus.Active)
            .Select(e => new
            {
                e.Id,
                e.ManagerId,
                e.FirstName,
                e.LastName,
                e.PositionProfileId,
                e.PhoneNumber,
                e.WorkEmail,
            })
            .ToListAsync(cancellationToken);

        var byManager = employees
            .Where(e => e.ManagerId is not null)
            .ToLookup(e => e.ManagerId!.Value);

        var team = new List<(Guid Id, string FirstName, string LastName, Guid PositionProfileId, string? PhoneNumber, string WorkEmail)>();

        if (includeIndirect)
        {
            var queue = new Queue<Guid>();
            queue.Enqueue(managerId);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var report in byManager[current])
                {
                    team.Add((report.Id, report.FirstName, report.LastName, report.PositionProfileId, report.PhoneNumber, report.WorkEmail));
                    queue.Enqueue(report.Id);
                }
            }
        }
        else
        {
            foreach (var report in byManager[managerId])
                team.Add((report.Id, report.FirstName, report.LastName, report.PositionProfileId, report.PhoneNumber, report.WorkEmail));
        }

        var positionProfileIds = team.Select(e => e.PositionProfileId).ToHashSet();
        var positionProfileTitles = positionProfileIds.Count > 0
            ? await dbContext.PositionProfiles
                .AsNoTracking()
                .Where(p => positionProfileIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Title, cancellationToken)
            : new Dictionary<Guid, string>();

        var teamIds = team.Select(e => e.Id).ToList();
        var photoUrls = await profilePhotoReader.GetCurrentPhotoUrlsAsync(companyId, teamIds, cancellationToken);

        var items = team
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .Select(e => new TeamMemberItem(
                e.Id,
                $"{e.FirstName} {e.LastName}",
                positionProfileTitles.TryGetValue(e.PositionProfileId, out var title) ? title : null,
                e.PhoneNumber,
                e.WorkEmail,
                photoUrls.TryGetValue(e.Id, out var photoUrl) ? photoUrl : null))
            .ToList();

        return new GetMyTeamResponse(items);
    }
}
