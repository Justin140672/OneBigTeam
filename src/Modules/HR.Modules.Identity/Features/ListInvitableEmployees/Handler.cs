using HR.Modules.Employees.Contracts;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.ListInvitableEmployees;

internal sealed class ListInvitableEmployeesHandler(
    IdentityDbContext db,
    IEmployeeInviteCandidateReader inviteCandidateReader)
{
    public async Task<Result<ListInvitableEmployeesResponse>> HandleAsync(
        ListInvitableEmployeesRequest request,
        CancellationToken cancellationToken)
    {
        var candidates = await inviteCandidateReader.GetCandidatesAsync(request.CompanyId, cancellationToken);
        if (candidates.Count == 0)
            return Result.Success(new ListInvitableEmployeesResponse([]));

        var employeeIds = candidates.Select(c => c.EmployeeId).ToList();

        var accountIds = await db.Users
            .AsNoTracking()
            .Where(u => employeeIds.Contains(u.Id))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        var profileIds = await db.UserProfiles
            .AsNoTracking()
            .Where(p => employeeIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        var openInvites = await db.UserInvites
            .AsNoTracking()
            .Where(i => i.CompanyId == request.CompanyId
                && employeeIds.Contains(i.EmployeeId)
                && i.ClaimedAt == null
                && i.CancelledAt == null)
            .ToListAsync(cancellationToken);

        // An actionable (still-pending, not expired) invite blocks a new one; an expired invite
        // does not — the employee can be re-invited, which supersedes the stale row.
        var pendingInviteEmployeeIds = openInvites
            .Where(i => !i.IsExpired)
            .Select(i => i.EmployeeId)
            .ToHashSet();

        var excluded = accountIds.Concat(profileIds).Concat(pendingInviteEmployeeIds).ToHashSet();

        var items = candidates
            .Where(c => !excluded.Contains(c.EmployeeId))
            .Select(c => new InvitableEmployeeItem(
                c.EmployeeId,
                c.FullName,
                c.WorkEmail,
                c.PositionProfileId,
                c.PositionTitle))
            .ToList();

        return Result.Success(new ListInvitableEmployeesResponse(items));
    }
}
