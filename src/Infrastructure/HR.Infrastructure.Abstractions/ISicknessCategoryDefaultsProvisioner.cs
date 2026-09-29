namespace HR.Infrastructure.Abstractions;

public interface ISicknessCategoryDefaultsProvisioner
{
    Task EnsureDefaultSicknessCategoriesAsync(Guid companyId, CancellationToken cancellationToken);
}
