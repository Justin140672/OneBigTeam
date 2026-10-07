using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetMyTeam;

internal sealed class GetMyTeamHandler(
    EmployeesDbContext dbContext,
    IProfilePhotoReader profilePhotoReader,
    IEmployeeSicknessStatusReader sicknessStatusReader,
    IEmployeeLeaveStatusReader leaveStatusReader)
{
    public async Task<GetMyTeamResponse> HandleAsync(
        Guid companyId, Guid managerId, bool includeIndirect, CancellationToken cancellationToken)
    {
        var employees = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && (e.Status == EmploymentStatus.Active || e.Status == EmploymentStatus.Leaving))
            .Select(e => new
            {
                e.Id,
                e.ManagerId,
                e.FirstName,
                e.LastName,
                e.PreferredName,
                e.PositionProfileId,
                e.PhoneNumber,
                e.WorkEmail,
                IsLeaving = e.Status == EmploymentStatus.Leaving,
            })
            .ToListAsync(cancellationToken);

        var byManager = employees
            .Where(e => e.ManagerId is not null)
            .ToLookup(e => e.ManagerId!.Value);

        var team = new List<(Guid Id, string FirstName, string LastName, string? PreferredName, Guid PositionProfileId, string? PhoneNumber, string WorkEmail, bool IsLeaving)>();

        if (includeIndirect)
        {
            var queue = new Queue<Guid>();
            queue.Enqueue(managerId);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var report in byManager[current])
                {
                    team.Add((report.Id, report.FirstName, report.LastName, report.PreferredName, report.PositionProfileId, report.PhoneNumber, report.WorkEmail, report.IsLeaving));
                    queue.Enqueue(report.Id);
                }
            }
        }
        else
        {
            foreach (var report in byManager[managerId])
                team.Add((report.Id, report.FirstName, report.LastName, report.PreferredName, report.PositionProfileId, report.PhoneNumber, report.WorkEmail, report.IsLeaving));
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
        var sickIds = await sicknessStatusReader.GetSickEmployeeIdsAsync(companyId, teamIds, cancellationToken);
        var onLeaveIds = await leaveStatusReader.GetOnLeaveTodayEmployeeIdsAsync(companyId, teamIds, cancellationToken);

        var items = team
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .Select(e => new TeamMemberItem(
                e.Id,
                PersonName.Display(e.FirstName, e.LastName, e.PreferredName),
                positionProfileTitles.TryGetValue(e.PositionProfileId, out var title) ? title : null,
                e.PhoneNumber,
                e.WorkEmail,
                photoUrls.TryGetValue(e.Id, out var photoUrl) ? photoUrl : null,
                sickIds.Contains(e.Id) ? "Sick" : onLeaveIds.Contains(e.Id) ? "OnLeave" : "AtWork",
                e.IsLeaving))
            .ToList();

        return new GetMyTeamResponse(items);
    }
}
