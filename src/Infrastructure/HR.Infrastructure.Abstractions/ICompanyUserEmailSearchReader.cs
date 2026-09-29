namespace HR.Infrastructure.Abstractions;

public interface ICompanyUserEmailSearchReader
{
    Task<IReadOnlyCollection<Guid>> FindCompanyIdsByEmailAsync(
        string searchTerm,
        CancellationToken cancellationToken);
}
