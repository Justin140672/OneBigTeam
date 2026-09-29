using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Persistence;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Services;

internal sealed class LegalHoldStatusReader(CompaniesDbContext dbContext) : ILegalHoldStatusReader
{
    public async Task<bool> IsUnderLegalHoldAsync(Guid companyId, CancellationToken cancellationToken)
    {
        return await dbContext.CustomerSubscriptions
            .AsNoTracking()
            .AnyAsync(s => s.CompanyId == companyId && s.LegalHoldPlacedAt != null, cancellationToken);
    }
}
