using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Services;

internal sealed class SupportSessionStateValidator(CompaniesDbContext dbContext, IClock clock) : ISupportSessionStateValidator
{
    public async Task<bool> IsActiveAsync(
        Guid supportSessionId,
        Guid companyId,
        Guid adminUserId,
        string? adminEmail,
        CancellationToken cancellationToken)
    {
        var session = await dbContext.SupportSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == supportSessionId, cancellationToken);

        if (session is null
            || session.CompanyId != companyId
            || session.IssuedByAdminUserId != adminUserId
            || !string.Equals(session.IssuedByAdminEmail, adminEmail, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return session.GrantsAccess(clock.UtcNowOffset());
    }
}
