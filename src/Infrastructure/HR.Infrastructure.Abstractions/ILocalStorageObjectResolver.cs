namespace HR.Infrastructure.Abstractions;

public interface ILocalStorageObjectResolver
{
    string Bucket { get; }

    Task<bool> IsServableAsync(string storageKey, CancellationToken cancellationToken);
}
