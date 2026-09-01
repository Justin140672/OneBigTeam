using HR.Infrastructure.Abstractions;

namespace HR.Infrastructure.Storage;

/// <summary>
/// Development/test implementation of <see cref="IOrganisationDataExportStorage"/> that stores
/// export ZIP archives on the local file system. Mirrors
/// <see cref="LocalSupportAttachmentStorageService"/>. Replace with
/// <see cref="SupabaseOrganisationDataExportStorage"/> for hosted environments.
/// </summary>
internal sealed class LocalOrganisationDataExportStorage : IOrganisationDataExportStorage
{
    private readonly string _basePath =
        Path.Combine(Path.GetTempPath(), "onebigteam", "organisation-exports");

    public async Task<string> UploadAsync(Guid companyId, Guid exportId, Stream content, CancellationToken cancellationToken)
    {
        var storageKey = $"organisation-exports/{companyId}/{exportId}.zip";
        var fullPath = ToFullPath(storageKey);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var file = File.Create(fullPath);
        await content.CopyToAsync(file, cancellationToken);

        return storageKey;
    }

    public Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken)
    {
        var fullPath = ToFullPath(storageKey);
        if (!File.Exists(fullPath))
            return Task.FromResult<Stream?>(null);

        return Task.FromResult<Stream?>(File.OpenRead(fullPath));
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        var fullPath = ToFullPath(storageKey);
        if (File.Exists(fullPath))
            File.Delete(fullPath);

        return Task.CompletedTask;
    }

    private string ToFullPath(string storageKey) =>
        Path.Combine(_basePath, storageKey.Replace('/', Path.DirectorySeparatorChar));
}
