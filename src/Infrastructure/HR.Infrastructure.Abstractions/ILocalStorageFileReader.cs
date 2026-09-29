namespace HR.Infrastructure.Abstractions;

public interface ILocalStorageFileReader
{
    Task<Stream?> OpenLocalReadStreamAsync(string storageKey, CancellationToken cancellationToken);
}
