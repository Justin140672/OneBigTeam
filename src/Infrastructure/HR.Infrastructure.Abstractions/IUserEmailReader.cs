namespace HR.Infrastructure.Abstractions;

public interface IUserEmailReader
{
    /// <summary>
    /// Resolves a user's email address by UserId within a company. Returns null if the user
    /// is not found or belongs to a different company.
    /// </summary>
    Task<string?> GetEmailAsync(
        Guid companyId,
        Guid userId,
        CancellationToken cancellationToken);
}
