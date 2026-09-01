namespace HR.Modules.Reporting.Features.DownloadOrganisationDataExport;

internal sealed record DownloadOrganisationDataExportRequest
{
    public Guid CompanyId { get; init; }
    public Guid ExportId { get; init; }
}
