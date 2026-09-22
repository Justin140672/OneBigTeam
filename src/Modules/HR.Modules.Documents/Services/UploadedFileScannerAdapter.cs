using HR.Infrastructure.Abstractions;

namespace HR.Modules.Documents.Services;

/// <summary>
/// Security review ticket 4 (P1): exposes this module's already environment-gated
/// <see cref="IVirusScanService"/> (ClamAv in Staging/Production, no-op only in
/// Development/explicit-test — see DocumentsModule.AddStorageService) to other modules via the
/// shared <see cref="IUploadedFileScanner"/> contract in HR.Infrastructure.Abstractions, without
/// granting those modules a direct (forbidden) reference to this module's project.
/// </summary>
internal sealed class UploadedFileScannerAdapter(IVirusScanService inner) : IUploadedFileScanner
{
    public async Task<UploadedFileScanResult> ScanAsync(
        Stream content, string fileName, CancellationToken cancellationToken)
    {
        var result = await inner.ScanAsync(content, fileName, cancellationToken);
        return result.IsClean
            ? UploadedFileScanResult.Clean()
            : UploadedFileScanResult.Infected(result.ThreatName ?? "Unknown threat");
    }
}
