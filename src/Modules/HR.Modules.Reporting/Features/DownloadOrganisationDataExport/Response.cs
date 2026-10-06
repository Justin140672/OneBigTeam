namespace HR.Modules.Reporting.Features.DownloadOrganisationDataExport;

/// <summary>
/// A live, forward-only storage stream. Ownership transfers to the caller, which must dispose it
/// (releasing the underlying storage connection) once the response completes or is aborted.
/// </summary>
internal sealed record DownloadOrganisationDataExportResult(
    Stream Content,
    long? ContentLength,
    string FileName,
    string ContentType);
