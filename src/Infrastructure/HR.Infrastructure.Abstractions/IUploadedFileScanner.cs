namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Security review ticket 4 (P1): a shared, cross-module malware-scanning contract so any module
/// that accepts file uploads (e.g. HR.Modules.Support) can route them through the same scanner
/// HR.Modules.Documents already wires up for ClamAv/no-op (ticket 2), without a forbidden direct
/// project reference to HR.Modules.Documents. HR.Modules.Documents registers the implementation
/// against this interface; consuming modules only ever see the abstraction.
/// </summary>
public interface IUploadedFileScanner
{
    Task<UploadedFileScanResult> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken);
}

public enum UploadedFileScanStatus
{
    Clean,
    Infected,
}

public sealed record UploadedFileScanResult(UploadedFileScanStatus Status, string? ThreatName)
{
    public bool IsClean => Status == UploadedFileScanStatus.Clean;

    public static UploadedFileScanResult Clean() => new(UploadedFileScanStatus.Clean, null);

    public static UploadedFileScanResult Infected(string threatName) =>
        new(UploadedFileScanStatus.Infected, threatName);
}
