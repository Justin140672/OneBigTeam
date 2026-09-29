namespace HR.Infrastructure.Abstractions;

public interface IUserEmailReader
{
    Task<string?> GetEmailAsync(
        Guid companyId,
        Guid userId,
        CancellationToken cancellationToken);
}
