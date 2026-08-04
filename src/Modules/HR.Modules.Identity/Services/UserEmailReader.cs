using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Services;

internal sealed class UserEmailReader(IdentityDbContext dbContext) : IUserEmailReader
{
    public async Task<string?> GetEmailAsync(
        Guid companyId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await dbContext.UserProfiles
            .AsNoTracking()
            .Where(u => u.Id == userId && u.CompanyId == companyId)
            .Select(u => u.Email)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
