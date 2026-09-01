namespace HR.Modules.Reporting.Features.GetLatestOrganisationDataExport;

internal sealed record GetLatestOrganisationDataExportRequest
{
    public Guid CompanyId { get; init; }
}
