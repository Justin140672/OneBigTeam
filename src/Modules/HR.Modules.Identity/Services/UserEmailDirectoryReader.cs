using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Persistence;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Services;

internal sealed class UserEmailDirectoryReader(IdentityDbContext dbContext) : IUserEmailDirectoryReader
{
    public async Task<IReadOnlyDictionary<Guid, string>> GetEmailsByUserIdsAsync(
        IReadOnlyCollection<Guid> supabaseAuthUserIds,
        CancellationToken cancellationToken)
    {
        var ids = supabaseAuthUserIds.Distinct().ToList();

        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        return await dbContext.UserProfiles
            .AsNoTracking()
            .Where(u => ids.Contains(u.SupabaseAuthUserId))
            .ToDictionaryAsync(u => u.SupabaseAuthUserId, u => u.Email, cancellationToken);
    }

    public async Task<IReadOnlyCollection<Guid>> FindUserIdsByEmailAsync(
        string searchTerm,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return [];

        return await dbContext.UserProfiles
            .AsNoTracking()
            .Where(u => EF.Functions.ILike(u.Email, $"%{searchTerm}%"))
            .Select(u => u.SupabaseAuthUserId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}
