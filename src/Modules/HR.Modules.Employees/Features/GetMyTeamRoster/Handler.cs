using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetMyTeamRoster;

internal sealed class GetMyTeamRosterHandler(
    EmployeesDbContext dbContext,
    IProfilePhotoReader profilePhotoReader)
{
    public async Task<GetMyTeamRosterResponse> HandleAsync(
        Guid companyId, Guid managerId, bool includeIndirect, CancellationToken cancellationToken)
    {
        var employees = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.Status != EmploymentStatus.FormerEmployee)
            .Select(e => new
            {
                e.Id,
                e.ManagerId,
                e.FirstName,
                e.LastName,
                e.PreferredName,
                e.PositionProfileId,
                e.WorkEmail,
                e.Status,
            })
            .ToListAsync(cancellationToken);

        var byManager = employees
            .Where(e => e.ManagerId is not null)
            .ToLookup(e => e.ManagerId!.Value);

        var team = new List<(Guid Id, string FirstName, string LastName, string? PreferredName, Guid PositionProfileId, string WorkEmail, EmploymentStatus Status)>();

        if (includeIndirect)
        {
            var queue = new Queue<Guid>();
            queue.Enqueue(managerId);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var report in byManager[current])
                {
                    team.Add((report.Id, report.FirstName, report.LastName, report.PreferredName, report.PositionProfileId, report.WorkEmail, report.Status));
                    queue.Enqueue(report.Id);
                }
            }
        }
        else
        {
            foreach (var report in byManager[managerId])
                team.Add((report.Id, report.FirstName, report.LastName, report.PreferredName, report.PositionProfileId, report.WorkEmail, report.Status));
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
            .Select(e => new TeamRosterItem(
                e.Id,
                e.FirstName,
                e.LastName,
                e.PreferredName,
                positionProfileTitles.TryGetValue(e.PositionProfileId, out var title) ? title : null,
                e.WorkEmail,
                photoUrls.TryGetValue(e.Id, out var photoUrl) ? photoUrl : null,
                e.Status))
            .ToList();

        return new GetMyTeamRosterResponse(items);
    }
}
