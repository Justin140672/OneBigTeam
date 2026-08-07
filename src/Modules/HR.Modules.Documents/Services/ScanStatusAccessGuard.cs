using HR.Modules.Documents.Domain;
using HR.SharedKernel;

namespace HR.Modules.Documents.Services;

/// <summary>
/// Single reusable access-control check used by every download/read endpoint that resolves a
/// storage URL for a scannable file (Document, EmployeeProfilePhoto, PendingProfilePhoto,
/// SharedCompanyDocument, SharedCompanyDocumentVersion). A file must never be handed out for
/// download unless its virus scan came back Clean.
///
/// Infected/Failed files will already have had their underlying storage object deleted by
/// ScanUploadedFileJob — this check exists so callers get a friendly Result failure instead of a
/// broken redirect or a storage 404.
/// </summary>
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
