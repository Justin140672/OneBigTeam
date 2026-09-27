namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Implemented only by Development/test <c>Local*StorageService</c> fallbacks. Lets trusted
/// server-side code (the malware-scan job) read a not-yet-Clean file straight from local disk instead
/// of fetching it over the dev delivery route — which, by design, refuses anything that is not Clean.
/// Production (Supabase) storage services never implement this, so their code paths are unchanged.
/// </summary>
public interface ILocalStorageFileReader
{
    /// <summary>Opens the stored file for reading, or returns null when it does not exist.</summary>
    Task<Stream?> OpenLocalReadStreamAsync(string storageKey, CancellationToken cancellationToken);
}
