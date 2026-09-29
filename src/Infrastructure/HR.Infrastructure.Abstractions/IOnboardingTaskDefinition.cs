namespace HR.Infrastructure.Abstractions;

public interface IOnboardingTaskDefinition
{
    string Key { get; }

    string Name { get; }

    string Description { get; }

    bool IsMandatory { get; }

    int Order { get; }

    Task<string> GetLinkUrlAsync(Guid companyId, CancellationToken cancellationToken);

    Task<bool> IsCompletedAsync(Guid companyId, CancellationToken cancellationToken);
}
