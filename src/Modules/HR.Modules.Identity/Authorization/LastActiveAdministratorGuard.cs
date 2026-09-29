using HR.Modules.Employees.Contracts;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Authorization;

internal sealed class LastActiveAdministratorGuard(IdentityDbContext db, IEmployeeAudienceReader employeeAudienceReader)
{
    public async Task<bool> HasOtherActiveHolderAsync(
        Guid companyId, Guid roleId, Guid excludeUserId, CancellationToken cancellationToken)
    {
        var companyEmployeeIds = await employeeAudienceReader.GetAllEmployeeIdsAsync(companyId, cancellationToken);
        return await HasOtherActiveHolderAsync(roleId, excludeUserId, companyEmployeeIds, cancellationToken);
    }

    public async Task<bool> HasOtherActiveHolderAsync(
        Guid roleId, Guid excludeUserId, IReadOnlyList<Guid> companyEmployeeIds, CancellationToken cancellationToken)
    {
        var otherHolderIds = await db.UserRoles
            .Where(ur => ur.RoleId == roleId && ur.UserId != excludeUserId && companyEmployeeIds.Contains(ur.UserId))
            .Select(ur => ur.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (otherHolderIds.Count == 0)
            return false;

        return await db.UserProfiles
            .AnyAsync(p => otherHolderIds.Contains(p.Id) && p.IsActive, cancellationToken);
    }
}
