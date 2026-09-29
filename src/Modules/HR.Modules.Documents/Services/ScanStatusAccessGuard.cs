using HR.Modules.Documents.Domain;
using HR.SharedKernel;

namespace HR.Modules.Documents.Services;

internal static class ScanStatusAccessGuard
{
    public static Error? CheckDownloadable(FileScanStatus status) => status switch
    {
        FileScanStatus.Clean => null,
        FileScanStatus.Pending or FileScanStatus.Scanning =>
            Error.Validation("This document is currently being security checked."),
        FileScanStatus.Infected or FileScanStatus.Failed =>
            Error.Validation("This document failed a security scan."),
        _ => Error.Validation("This document failed a security scan."),
    };
}
