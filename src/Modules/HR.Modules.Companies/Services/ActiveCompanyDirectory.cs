using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Services;

internal sealed class ActiveCompanyDirectory(CompaniesDbContext dbContext) : IActiveCompanyDirectory
{
    public async Task<IReadOnlyList<Guid>> GetActiveCompanyIdsAsync(CancellationToken cancellationToken)
        => await dbContext.Companies
            .AsNoTracking()
            .Where(c => c.Status == CompanyStatus.Active)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
}
