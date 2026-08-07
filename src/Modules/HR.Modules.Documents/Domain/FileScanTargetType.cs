namespace HR.Modules.Documents.Domain;

/// <summary>
/// Which scannable entity kind a ScanUploadedFileJob invocation targets. Kept as a simple enum
/// (rather than five separate job classes) so there is a single enqueue-style Hangfire job for
/// this feature, matching the ticket's "no scanner-specific coupling in the job" requirement.
/// </summary>
internal enum FileScanTargetType
{
    Document = 0,
    EmployeeProfilePhoto = 1,
    PendingProfilePhoto = 2,
    SharedCompanyDocument = 3,
    SharedCompanyDocumentVersion = 4,
}
