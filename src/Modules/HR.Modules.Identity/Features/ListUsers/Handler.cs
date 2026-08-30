using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.ListUsers;

internal sealed class ListUsersHandler(
    IdentityDbContext db,
    IEmployeeNameReader employeeNameReader,
    IEmployeeAudienceReader employeeAudienceReader,
    IPositionProfileReader positionProfileReader)
{
    public async Task<Result<ListUsersResponse>> HandleAsync(ListUsersRequest request, CancellationToken cancellationToken)
    {
        // Build one row per employee in the company, invited or not — starting from the invite
        // table alone (the original approach) silently dropped every ApplicationUser that was
        // never routed through the invite flow, e.g. dev-seeded personas created directly in
        // IdentityModule's seed data. GetAllEmployeeIdsAsync (not GetEligibleEmployeeIdsAsync,
        // which is Active-only and built for document-audience matching) is used deliberately here
        // — a Draft/Suspended employee can still have or need a user account.
        var employeeIds = await employeeAudienceReader.GetAllEmployeeIdsAsync(request.CompanyId, cancellationToken);

        var invites = await db.UserInvites
            .AsNoTracking()
            .Where(i => i.CompanyId == request.CompanyId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

        var latestInviteByEmployee = invites
            .GroupBy(i => i.EmployeeId)
            .ToDictionary(g => g.Key, g => g.First());

        var users = await db.Users
            .AsNoTracking()
            .Where(u => employeeIds.Contains(u.Id))
            .ToListAsync(cancellationToken);
        var usersById = users.ToDictionary(u => u.Id);

        // Real Supabase-backed accounts (self-service SignUp, AcceptInvite) live in UserProfiles,
        // never in Users (ApplicationUser) — without this, an admin who signed themselves up, or
        // any employee who accepted a real invite, never appeared in this list at all (no
        // ApplicationUser row to find, and — for SignUp specifically — no UserInvite row either,
        // so the "invite is null && user is null" skip below dropped them silently).
        var profiles = await db.UserProfiles
            .AsNoTracking()
            .Where(p => employeeIds.Contains(p.Id))
            .ToListAsync(cancellationToken);
        var profilesById = profiles.ToDictionary(p => p.Id);

        var userRoles = await db.UserRoles
            .AsNoTracking()
            .Where(ur => employeeIds.Contains(ur.UserId))
            .ToListAsync(cancellationToken);

        var roles = await db.Roles.AsNoTracking().ToListAsync(cancellationToken);
        var roleNamesById = roles.ToDictionary(r => r.Id, r => r.Name);

        var names = await employeeNameReader.GetNamesAsync(request.CompanyId, employeeIds, cancellationToken);

        // ADM-01: surface each linked employee's current position on the list.
        var audienceProfiles = await employeeAudienceReader.GetEmployeeAudienceProfilesAsync(
            request.CompanyId, employeeIds, cancellationToken);
        var positionProfileIdByEmployee = audienceProfiles
            .Where(kvp => kvp.Value.PositionProfileId is not null)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.PositionProfileId!.Value);
        var distinctPositionProfileIds = positionProfileIdByEmployee.Values.Distinct().ToList();
        var positionTitleById = distinctPositionProfileIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await positionProfileReader.GetSummariesAsync(request.CompanyId, distinctPositionProfileIds, cancellationToken))
                .ToDictionary(p => p.Id, p => p.Title);

        var rows = new List<UserAdministrationListItem>();

        foreach (var employeeId in employeeIds)
        {
            latestInviteByEmployee.TryGetValue(employeeId, out var invite);
            usersById.TryGetValue(employeeId, out var user);
            profilesById.TryGetValue(employeeId, out var profile);

            // No invite and no account for this employee — nothing to show them for yet.
            if (invite is null && user is null && profile is null)
                continue;

            var roleIds = userRoles.Where(ur => ur.UserId == employeeId).Select(ur => ur.RoleId).ToList();
            var roleNames = roleIds.Select(id => roleNamesById.GetValueOrDefault(id, "Unknown")).ToList();

            var name = names.TryGetValue(employeeId, out var employeeName)
                ? employeeName
                : user is not null ? $"{user.FirstName} {user.LastName}".Trim()
                : profile is not null ? $"{profile.FirstName} {profile.LastName}".Trim()
                : invite?.Email ?? string.Empty;

            // Same convention as GetUserDetailsHandler: a user with no tracked invite record (e.g. a
            // dev-seeded persona created directly as an ApplicationUser) is treated as Claimed rather
            // than falling through to an invite-only status.
            string invitationStatus;
            if (invite is null)
                invitationStatus = "Claimed";
            else if (invite.IsCancelled)
                invitationStatus = "Cancelled";
            else if (invite.IsClaimed)
                invitationStatus = "Claimed";
            else if (invite.IsExpired)
                invitationStatus = "Expired";
            else
                invitationStatus = "Pending";

            // UserProfile (Supabase-backed) has no local IsActive/disable concept — its mere
            // existence means the account is active, same convention as
            // EmployeeUserAccountStatusReader's earlier fix for this account type.
            var accountStatus = user is not null
                ? user.IsActive ? "Active" : "Disabled"
                : profile is not null ? "Active" : "NoAccount";

            var email = user?.Email ?? profile?.Email ?? invite?.Email ?? string.Empty;

            positionProfileIdByEmployee.TryGetValue(employeeId, out var positionProfileId);
            var positionTitle = positionProfileId != Guid.Empty
                && positionTitleById.TryGetValue(positionProfileId, out var t)
                    ? t
                    : null;

            rows.Add(new UserAdministrationListItem(
                employeeId,
                user?.Id ?? profile?.Id,
                string.IsNullOrWhiteSpace(name) ? email : name,
                email,
                roleIds,
                roleNames,
                accountStatus,
                invitationStatus,
                invite?.Id,
                user?.LastLoginAt,
                invite?.CreatedAt ?? user?.CreatedAt ?? profile?.CreatedAt ?? DateTimeOffset.UtcNow,
                positionProfileId == Guid.Empty ? null : positionProfileId,
                positionTitle));
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
