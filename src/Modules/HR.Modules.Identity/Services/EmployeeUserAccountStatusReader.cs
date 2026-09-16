using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Services;

internal sealed class EmployeeUserAccountStatusReader(IdentityDbContext db) : IEmployeeUserAccountStatusReader
{
    public async Task<IReadOnlyDictionary<Guid, EmployeeUserAccountSummary>> GetStatusesAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        CancellationToken cancellationToken)
    {
        var ids = employeeIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, EmployeeUserAccountSummary>();

        var users = await db.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.IsActive, u.LastLoginAt })
            .ToListAsync(cancellationToken);

        // Real Supabase-backed accounts (AcceptInvite, self-service SignUp) live in UserProfiles,
        // not Users — an invited employee who's accepted their invite has a UserProfile row, never
        // an ApplicationUser one (see AcceptInvite/Endpoint.cs's remarks). Ticket 1 (P1) gave
        // UserProfile its own IsActive flag, so these now reflect real disablement instead of
        // always reading as Active. UserProfile has no LastLoginAt concept yet.
        var profiles = await db.UserProfiles
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.IsActive })
            .ToListAsync(cancellationToken);

        var invites = await db.UserInvites
            .AsNoTracking()
            .Where(i => i.CompanyId == companyId && ids.Contains(i.EmployeeId) && i.ClaimedAt == null && i.CancelledAt == null)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, EmployeeUserAccountSummary>();

        foreach (var user in users)
        {
            result[user.Id] = new EmployeeUserAccountSummary(
                user.Id,
                user.IsActive ? EmployeeUserAccountStatus.Active : EmployeeUserAccountStatus.Disabled,
                user.LastLoginAt);
        }

        foreach (var profile in profiles)
        {
            if (result.ContainsKey(profile.Id))
                continue; // an ApplicationUser already exists — that status takes precedence.

            result[profile.Id] = new EmployeeUserAccountSummary(
                profile.Id,
                profile.IsActive ? EmployeeUserAccountStatus.Active : EmployeeUserAccountStatus.Disabled,
                LastLoginAt: null);
        }

        foreach (var invite in invites)
        {
            if (result.ContainsKey(invite.EmployeeId))
                continue; // an ApplicationUser or UserProfile already exists — that status takes precedence.

            result[invite.EmployeeId] = new EmployeeUserAccountSummary(
                invite.EmployeeId,
                invite.IsExpired ? EmployeeUserAccountStatus.InvitationExpired : EmployeeUserAccountStatus.PendingInvitation,
                LastLoginAt: null);
        }

        return result;
    }
}
