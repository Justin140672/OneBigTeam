using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetManagerTeamStatusSummary;

/// <summary>
/// DSH-05 coordinating query for the Manager Dashboard "Team Status" widget. Composes this
/// module's own employee/hierarchy/working-pattern data with per-module cross-module contract
/// readers (leave, sickness, probation) into one authoritative summary — mirroring how ADM-02's
/// Compliance Centre composes readers — without HR.Modules.Employees referencing any other
/// module's implementation.
///
/// Scope is the manager's entire reporting sub-tree, resolved fresh by
/// <see cref="IDirectReportsReader.GetAllDescendantIdsAsync"/>
/// (specifications/architecture/11-manager-hierarchy-scope.md). Status is computed for "today" in
/// the company time zone. Counts and drill-down come from the same member list so they always
/// agree.
/// </summary>
internal sealed class GetManagerTeamStatusSummaryHandler(
    EmployeesDbContext dbContext,
    IDirectReportsReader directReportsReader,
    ICompanyLeaveSettingsReader companyLeaveSettingsReader,
    IEmployeeLeaveStatusReader leaveStatusReader,
    IEmployeesOffSickReader offSickReader,
    IEmployeesInProbationReader inProbationReader,
    IEmployeesMissingFitNoteReader missingFitNoteReader,
    IClock clock,
    ICompanyTimeZoneReader timeZoneReader)
{
    public async Task<GetManagerTeamStatusSummaryResponse> HandleAsync(
        Guid companyId, Guid managerId, CancellationToken cancellationToken)
    {
        var today = await CompanyToday.ResolveAsync(companyId, clock, timeZoneReader, cancellationToken);

        var subtreeIds = await directReportsReader.GetAllDescendantIdsAsync(companyId, managerId, cancellationToken);
        if (subtreeIds.Count == 0)
            return Empty();

        var subtreeSet = subtreeIds.ToHashSet();

        // Counted population: exclude non-active (Draft / Suspended / Leaving / former), not-yet-
        // started, and already-left employees.
        var candidates = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId
                     && subtreeSet.Contains(e.Id)
                     && e.Status == EmploymentStatus.Active
                     && e.StartDate <= today
                     && (e.LeavingDate == null || e.LeavingDate >= today))
            .Select(e => new
            {
                e.Id,
                e.FirstName,
                e.LastName,
                e.PositionProfileId,
                e.WorkingDaysOverride,
                e.HoursPerDayOverride,
            })
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
            return Empty();

        var memberIds = candidates.Select(c => c.Id).ToList();

        var positionProfileIds = candidates.Select(c => c.PositionProfileId).ToHashSet();
        var profilePatterns = await dbContext.PositionProfiles
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId && positionProfileIds.Contains(p.Id))
            .Select(p => new { p.Id, p.WorkingDaysOverride, p.HoursPerDayOverride })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var companyPattern = (await companyLeaveSettingsReader.GetLeaveSettingsAsync(companyId, cancellationToken))
            .WorkingPattern;

        var jobTitles = await dbContext.PositionProfiles
            .AsNoTracking()
            .Where(p => positionProfileIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Title, cancellationToken);

        var onLeaveIds = await leaveStatusReader.GetOnLeaveTodayEmployeeIdsAsync(companyId, memberIds, cancellationToken);
        var sickIds = await offSickReader.GetOffSickEmployeeIdsAsync(companyId, memberIds, today, cancellationToken);
        var probationIds = await inProbationReader.GetEmployeeIdsInProbationAsync(companyId, memberIds, cancellationToken);
        var fitNoteIds = await missingFitNoteReader.GetEmployeeIdsMissingFitNotesAsync(companyId, memberIds, cancellationToken);

        var members = candidates
            .OrderBy(c => c.LastName)
            .ThenBy(c => c.FirstName)
            .Select(c =>
            {
                var onLeave = onLeaveIds.Contains(c.Id);
                var sick = sickIds.Contains(c.Id);
                var scheduled = ResolvePattern(
                        c.WorkingDaysOverride, c.HoursPerDayOverride,
                        profilePatterns.TryGetValue(c.PositionProfileId, out var pp)
                            ? (pp.WorkingDaysOverride, pp.HoursPerDayOverride)
                            : (null, null),
                        companyPattern)
                    .IsWorkingDay(today.DayOfWeek);

                var primary = sick ? "Sick"
                    : onLeave ? "OnLeave"
                    : !scheduled ? "NotScheduled"
                    : "AtWork";

                return new TeamMemberStatusItem(
                    c.Id,
                    $"{c.FirstName} {c.LastName}",
                    jobTitles.TryGetValue(c.PositionProfileId, out var title) ? title : null,
                    onLeave,
                    sick,
                    probationIds.Contains(c.Id),
                    fitNoteIds.Contains(c.Id),
                    scheduled,
                    primary);
            })
            .ToList();

        var awayToday = members.Count(m => m.OnLeaveToday || m.OffSickToday);

        return new GetManagerTeamStatusSummaryResponse(
            TeamSize: members.Count,
            AtWork: members.Count(m => m.PrimaryStatus == "AtWork"),
            AwayToday: awayToday,
            OnLeave: members.Count(m => m.OnLeaveToday),
            Sick: members.Count(m => m.OffSickToday),
            InProbation: members.Count(m => m.InProbation),
            MissingFitNotes: members.Count(m => m.MissingFitNote),
            NotScheduledToday: members.Count(m => m.PrimaryStatus == "NotScheduled"),
            Members: members);
    }

    // Mirrors WorkingPatternProvider: an override level only applies when BOTH its working-days
    // and hours-per-day values are present; otherwise fall through to the next level.
    private static WorkingPattern ResolvePattern(
        WorkingDays? employeeDays, decimal? employeeHours,
        (WorkingDays? Days, decimal? Hours) profile,
        WorkingPattern companyPattern)
    {
        if (employeeDays is not null && employeeHours is not null)
            return new WorkingPattern(employeeDays.Value, employeeHours.Value);

        if (profile.Days is not null && profile.Hours is not null)
            return new WorkingPattern(profile.Days.Value, profile.Hours.Value);

        return companyPattern;
    }

    private static GetManagerTeamStatusSummaryResponse Empty() =>
        new(0, 0, 0, 0, 0, 0, 0, 0, []);
}
