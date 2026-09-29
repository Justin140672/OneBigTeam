namespace HR.Infrastructure.Abstractions;

public interface IUserEmailDirectoryReader
{
    Task<IReadOnlyDictionary<Guid, string>> GetEmailsByUserIdsAsync(
        IReadOnlyCollection<Guid> supabaseAuthUserIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<Guid>> FindUserIdsByEmailAsync(
        string searchTerm,
        CancellationToken cancellationToken);
}
