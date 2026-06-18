namespace HR.Modules.Documents.Services;

internal interface IVirusScanService
{
    Task<VirusScanResult> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken);
}
