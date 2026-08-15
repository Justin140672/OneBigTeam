using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Persistence;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Services;

internal sealed class PlatformUserActivityReader(IdentityDbContext dbContext) : IPlatformUserActivityReader
{
    public Task<int> GetTotalUserCountAsync(CancellationToken cancellationToken)
    {
        return dbContext.UserProfiles
            .AsNoTracking()
            .CountAsync(cancellationToken);
    }
}
