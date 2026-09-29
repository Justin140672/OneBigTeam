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
        // table alone (the original approach) silently dropped every account that was
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
            profilesById.TryGetValue(employeeId, out var profile);

            if (invite is null && profile is null)
                continue;

            var roleIds = userRoles.Where(ur => ur.UserId == employeeId).Select(ur => ur.RoleId).ToList();
            var roleNames = roleIds.Select(id => roleNamesById.GetValueOrDefault(id, "Unknown")).ToList();

            var name = names.TryGetValue(employeeId, out var employeeName)
                ? employeeName
                : profile is not null ? $"{profile.FirstName} {profile.LastName}".Trim()
                : invite?.Email ?? string.Empty;

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

            var accountStatus = profile is not null ? (profile.IsActive ? "Active" : "Disabled") : "NoAccount";

            var email = profile?.Email ?? invite?.Email ?? string.Empty;

            positionProfileIdByEmployee.TryGetValue(employeeId, out var positionProfileId);
            var positionTitle = positionProfileId != Guid.Empty
                && positionTitleById.TryGetValue(positionProfileId, out var t)
                    ? t
                    : null;

            rows.Add(new UserAdministrationListItem(
                employeeId,
                profile?.Id,
                string.IsNullOrWhiteSpace(name) ? email : name,
                email,
                roleIds,
                roleNames,
                accountStatus,
                invitationStatus,
                invite?.Id,
                profile?.LastLoginAt,
                invite?.CreatedAt ?? profile?.CreatedAt ?? DateTimeOffset.UtcNow,
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
