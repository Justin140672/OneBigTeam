namespace HR.Modules.Reporting.Features.DownloadOrganisationDataExport;

internal sealed record DownloadOrganisationDataExportResult(
    byte[] Content,
    string FileName,
    string ContentType);
