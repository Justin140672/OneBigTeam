using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.ListPlatformAdministrators;

internal sealed class ListPlatformAdministratorsHandler(IdentityDbContext db)
{
    public async Task<Result<ListPlatformAdministratorsResponse>> HandleAsync(
        ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        if (!await IsEnabledPlatformAdministratorAsync(db, currentUser, cancellationToken))
            return Result.Failure<ListPlatformAdministratorsResponse>(
                Error.Unauthorized("Only an enabled platform administrator may view administrator accounts."));

        var administrators = await db.PlatformAdministrators
            .AsNoTracking()
            .OrderBy(a => a.Email)
            .Select(a => new PlatformAdministratorSummary(a.Id, a.Email, a.Role, a.IsEnabled, a.CreatedAt, a.DisabledAt))
            .ToListAsync(cancellationToken);

        return Result.Success(new ListPlatformAdministratorsResponse(administrators));
    }

    private static async Task<bool> IsEnabledPlatformAdministratorAsync(
        IdentityDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentUser.Email))
            return false;

        var normalizedEmail = currentUser.Email.Trim().ToLowerInvariant();
        return await db.PlatformAdministrators.AnyAsync(
            a => a.Email == normalizedEmail && a.IsEnabled, cancellationToken);
    }
}
