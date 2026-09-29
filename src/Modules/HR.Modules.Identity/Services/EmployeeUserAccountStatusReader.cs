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

        var profiles = await db.UserProfiles
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.IsActive, p.LastLoginAt })
            .ToListAsync(cancellationToken);

        var invites = await db.UserInvites
            .AsNoTracking()
            .Where(i => i.CompanyId == companyId && ids.Contains(i.EmployeeId) && i.ClaimedAt == null && i.CancelledAt == null)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, EmployeeUserAccountSummary>();

        foreach (var profile in profiles)
        {
            result[profile.Id] = new EmployeeUserAccountSummary(
                profile.Id,
                profile.IsActive ? EmployeeUserAccountStatus.Active : EmployeeUserAccountStatus.Disabled,
                profile.LastLoginAt);
        }

        foreach (var invite in invites)
        {
            if (result.ContainsKey(invite.EmployeeId))
                continue;

            result[invite.EmployeeId] = new EmployeeUserAccountSummary(
                invite.EmployeeId,
                invite.IsExpired ? EmployeeUserAccountStatus.InvitationExpired : EmployeeUserAccountStatus.PendingInvitation,
                LastLoginAt: null);
        }

        return result;
    }
}
