using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Persistence;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Services;

internal sealed class CompanyUserEmailSearchReader(IdentityDbContext dbContext) : ICompanyUserEmailSearchReader
{
    public async Task<IReadOnlyCollection<Guid>> FindCompanyIdsByEmailAsync(
        string searchTerm,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return [];

        return await dbContext.UserProfiles
            .AsNoTracking()
            .Where(u => EF.Functions.ILike(u.Email, $"%{searchTerm}%"))
            .Select(u => u.CompanyId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}
