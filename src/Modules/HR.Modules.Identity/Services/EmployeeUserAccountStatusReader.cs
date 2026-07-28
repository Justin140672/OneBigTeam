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

        foreach (var invite in invites)
        {
            if (result.ContainsKey(invite.EmployeeId))
                continue; // an ApplicationUser already exists — that status takes precedence.

            result[invite.EmployeeId] = new EmployeeUserAccountSummary(
                invite.EmployeeId,
                invite.IsExpired ? EmployeeUserAccountStatus.InvitationExpired : EmployeeUserAccountStatus.PendingInvitation,
                LastLoginAt: null);
        }

        return result;
    }
}
