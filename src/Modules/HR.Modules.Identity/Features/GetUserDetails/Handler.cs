using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Authorization;
using HR.Modules.Identity.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.GetUserDetails;

internal sealed class GetUserDetailsHandler(
    IdentityDbContext db,
    IEmployeeNameReader employeeNameReader,
    IEmployeeAudienceReader employeeAudienceReader,
    IPositionProfileReader positionProfileReader,
    ITargetUserCompanyGuard targetUserCompanyGuard)
{
    public async Task<Result<GetUserDetailsResponse>> HandleAsync(GetUserDetailsRequest request, CancellationToken cancellationToken)
    {
        var isMember = await targetUserCompanyGuard.IsMemberAsync(request.CompanyId, request.EmployeeId, cancellationToken);
        if (!isMember)
            return Result.Failure<GetUserDetailsResponse>(Error.NotFound("No user or invitation found for this employee."));

        var invite = await db.UserInvites
            .AsNoTracking()
            .Where(i => i.CompanyId == request.CompanyId && i.EmployeeId == request.EmployeeId)
            .OrderByDescending(i => i.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var profile = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.EmployeeId, cancellationToken);

        if (invite is null && profile is null)
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
            : profile is not null ? $"{profile.FirstName} {profile.LastName}".Trim() : invite!.Email;

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

        var accountStatus = profile is null ? "NoAccount" : profile.IsActive ? "Active" : "Disabled";

        var createdByName = invite?.CreatedByUserId is { } actorId
            ? names.GetValueOrDefault(actorId)
            : null;

        var audience = await employeeAudienceReader.GetEmployeeAudienceAsync(
            request.CompanyId, request.EmployeeId, cancellationToken);
        Guid? positionProfileId = audience?.PositionProfileId;
        string? positionTitle = positionProfileId is { } ppId
            ? (await positionProfileReader.GetSummaryAsync(request.CompanyId, ppId, cancellationToken))?.Title
            : null;

        return Result.Success(new GetUserDetailsResponse(
            request.EmployeeId,
            profile?.Id,
            string.IsNullOrWhiteSpace(name) ? invite?.Email ?? string.Empty : name,
            profile?.Email ?? invite?.Email ?? string.Empty,
            roleIds,
            roleNames,
            accountStatus,
            invitationStatus,
            invite?.Id,
            invite?.ExpiresAt,
            createdByName,
            profile?.LastLoginAt,
            invite?.CreatedAt ?? profile!.CreatedAt,
            positionProfileId,
            positionTitle));
    }
}
