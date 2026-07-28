using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.ListUsers;

internal sealed class ListUsersHandler(
    IdentityDbContext db,
    IEmployeeNameReader employeeNameReader)
{
    public async Task<Result<ListUsersResponse>> HandleAsync(ListUsersRequest request, CancellationToken cancellationToken)
    {
        // Build one row per employee that has ever been invited (claimed or not), keyed by
        // EmployeeId. Employees never invited don't appear here — see IEmployeeUserAccountStatusReader
        // for the Employees-module-facing "NoUser" projection used by the Employee List column.
        var invites = await db.UserInvites
            .AsNoTracking()
            .Where(i => i.CompanyId == request.CompanyId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

        var latestInviteByEmployee = invites
            .GroupBy(i => i.EmployeeId)
            .ToDictionary(g => g.Key, g => g.First());

        var employeeIds = latestInviteByEmployee.Keys.ToList();

        var users = await db.Users
            .AsNoTracking()
            .Where(u => employeeIds.Contains(u.Id))
            .ToListAsync(cancellationToken);
        var usersById = users.ToDictionary(u => u.Id);

        var userRoles = await db.UserRoles
            .AsNoTracking()
            .Where(ur => employeeIds.Contains(ur.UserId))
            .ToListAsync(cancellationToken);

        var roles = await db.Roles.AsNoTracking().ToListAsync(cancellationToken);
        var roleNamesById = roles.ToDictionary(r => r.Id, r => r.Name);

        var names = await employeeNameReader.GetNamesAsync(request.CompanyId, employeeIds, cancellationToken);

        var rows = new List<UserAdministrationListItem>();

        foreach (var employeeId in employeeIds)
        {
            var invite = latestInviteByEmployee[employeeId];
            usersById.TryGetValue(employeeId, out var user);

            var roleIds = userRoles.Where(ur => ur.UserId == employeeId).Select(ur => ur.RoleId).ToList();
            var roleNames = roleIds.Select(id => roleNamesById.GetValueOrDefault(id, "Unknown")).ToList();

            var name = names.TryGetValue(employeeId, out var employeeName)
                ? employeeName
                : user is not null ? $"{user.FirstName} {user.LastName}".Trim() : invite.Email;

            var invitationStatus = invite.IsCancelled ? "Cancelled"
                : invite.IsClaimed ? "Claimed"
                : invite.IsExpired ? "Expired"
                : "Pending";

            var accountStatus = user is null
                ? "NoAccount"
                : user.IsActive ? "Active" : "Disabled";

            rows.Add(new UserAdministrationListItem(
                employeeId,
                user?.Id,
                string.IsNullOrWhiteSpace(name) ? invite.Email : name,
                user?.Email ?? invite.Email,
                roleIds,
                roleNames,
                accountStatus,
                invitationStatus,
                user?.LastLoginAt,
                invite.CreatedAt));
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            rows = rows
                .Where(r => r.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || r.Email.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        rows = rows.OrderByDescending(r => r.CreatedAt).ToList();

        var total = rows.Count;
        var page = rows
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return Result.Success(new ListUsersResponse(page, total, request.Page, request.PageSize));
    }
}
