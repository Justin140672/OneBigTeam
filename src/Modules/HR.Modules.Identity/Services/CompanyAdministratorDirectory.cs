using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Services;

internal sealed class CompanyAdministratorDirectory(IdentityDbContext dbContext) : ICompanyAdministratorDirectory
{
    public async Task<IReadOnlyList<Guid>> GetActiveCompanyAdministratorEmployeeIdsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        return await dbContext.UserProfiles
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.IsActive)
            .Join(
                dbContext.UserRoles.AsNoTracking().Where(r => r.RoleId == SystemRoles.CompanyAdministrator),
                profile => profile.Id,
                role => role.UserId,
                (profile, role) => profile.Id)
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}
