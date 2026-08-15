using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Persistence;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Services;

internal sealed class CompanyUserCountReader(IdentityDbContext dbContext) : ICompanyUserCountReader
{
    public async Task<int> GetUserCountAsync(Guid companyId, CancellationToken cancellationToken)
    {
        return await dbContext.UserProfiles
            .AsNoTracking()
            .Where(u => u.CompanyId == companyId)
            .CountAsync(cancellationToken);
    }
}
