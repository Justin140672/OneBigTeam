namespace HR.Modules.Documents.Services;

internal sealed class NoOpVirusScanService : IVirusScanService
{
    public Task<VirusScanResult> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken)
        => Task.FromResult(VirusScanResult.Clean());
}
