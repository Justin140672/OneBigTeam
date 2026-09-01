using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Persistence;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Services;

/// <summary>
/// NFR-07: implements <see cref="ILegalHoldStatusReader"/> over the platform-owned
/// customer_subscriptions table. A company with no subscription row is treated as not under hold.
/// </summary>
internal sealed class LegalHoldStatusReader(CompaniesDbContext dbContext) : ILegalHoldStatusReader
{
    public async Task<bool> IsUnderLegalHoldAsync(Guid companyId, CancellationToken cancellationToken)
    {
        return await dbContext.CustomerSubscriptions
            .AsNoTracking()
            .AnyAsync(s => s.CompanyId == companyId && s.LegalHoldPlacedAt != null, cancellationToken);
    }
}
