using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Services;

internal sealed class HrAdministratorDirectory(IdentityDbContext dbContext) : IHrAdministratorDirectory
{
    public async Task<IReadOnlyList<Guid>> GetHrAdministratorEmployeeIdsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        return await dbContext.UserProfiles
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId)
            .Join(
                dbContext.UserRoles.AsNoTracking().Where(r => r.RoleId == SystemRoles.HrAdministrator),
                profile => profile.Id,
                role => role.UserId,
                (profile, role) => profile.Id)
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}
