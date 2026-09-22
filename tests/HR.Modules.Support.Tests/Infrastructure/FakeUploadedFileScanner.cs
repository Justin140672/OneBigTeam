using HR.Infrastructure.Abstractions;

namespace HR.Modules.Support.Tests.Infrastructure;

internal sealed class FakeUploadedFileScanner : IUploadedFileScanner
{
    /// <summary>File names that this fake reports as infected — everything else is clean.</summary>
    public HashSet<string> InfectedFileNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool ThrowOnScan { get; set; }

    /// <summary>Reliability review issue 4 (P1): simulates the caller's own token being the source
    /// of cancellation, so tests can verify OperationCanceledException propagates rather than being
    /// swallowed into a generic scan-failure Result.</summary>
    public bool ThrowOperationCanceledOnScan { get; set; }

    public List<string> ScannedFileNames { get; } = [];

    public Task<UploadedFileScanResult> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        if (ThrowOperationCanceledOnScan)
            throw new OperationCanceledException("Simulated caller cancellation during scan.", cancellationToken);

        if (ThrowOnScan)
            throw new InvalidOperationException("Simulated scanner-unreachable failure.");

        ScannedFileNames.Add(fileName);

        return Task.FromResult(InfectedFileNames.Contains(fileName)
            ? UploadedFileScanResult.Infected("EICAR-Test-Signature")
            : UploadedFileScanResult.Clean());
    }
}
