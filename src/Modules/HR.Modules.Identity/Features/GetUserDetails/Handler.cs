using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.GetUserDetails;

internal sealed class GetUserDetailsHandler(
    IdentityDbContext db,
    IEmployeeNameReader employeeNameReader)
{
    public async Task<Result<GetUserDetailsResponse>> HandleAsync(GetUserDetailsRequest request, CancellationToken cancellationToken)
    {
        var invite = await db.UserInvites
            .AsNoTracking()
            .Where(i => i.CompanyId == request.CompanyId && i.EmployeeId == request.EmployeeId)
            .OrderByDescending(i => i.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.EmployeeId, cancellationToken);

        if (invite is null && user is null)
            return Result.Failure<GetUserDetailsResponse>(Error.NotFound("No user or invitation found for this employee."));

        var roleIds = await db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == request.EmployeeId)
            .Select(ur => ur.RoleId)
            .ToListAsync(cancellationToken);

        var roleNames = await db.Roles
            .AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Name)
            .ToListAsync(cancellationToken);

        var lookupIds = new List<Guid> { request.EmployeeId };
        if (invite?.CreatedByUserId is { } createdBy)
            lookupIds.Add(createdBy);

        var names = await employeeNameReader.GetNamesAsync(request.CompanyId, lookupIds, cancellationToken);

        var name = names.TryGetValue(request.EmployeeId, out var employeeName)
            ? employeeName
            : user is not null ? $"{user.FirstName} {user.LastName}".Trim() : invite!.Email;

        string invitationStatus;
        if (invite is null)
            invitationStatus = "Claimed"; // user exists without a tracked invite record (e.g. seeded dev persona)
        else if (invite.IsCancelled)
            invitationStatus = "Cancelled";
        else if (invite.IsClaimed)
            invitationStatus = "Claimed";
        else if (invite.IsExpired)
            invitationStatus = "Expired";
        else
            invitationStatus = "Pending";

        var accountStatus = user is null ? "NoAccount" : user.IsActive ? "Active" : "Disabled";

        var createdByName = invite?.CreatedByUserId is { } actorId
            ? names.GetValueOrDefault(actorId)
            : null;

        return Result.Success(new GetUserDetailsResponse(
            request.EmployeeId,
            user?.Id,
            string.IsNullOrWhiteSpace(name) ? invite?.Email ?? string.Empty : name,
            user?.Email ?? invite?.Email ?? string.Empty,
            roleIds,
            roleNames,
            accountStatus,
            invitationStatus,
            invite?.Id,
            invite?.ExpiresAt,
            createdByName,
            user?.LastLoginAt,
            invite?.CreatedAt ?? user!.CreatedAt));
    }
}
