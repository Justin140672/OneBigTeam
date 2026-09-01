namespace HR.Modules.Reporting.Features.RequestOrganisationDataExport;

internal sealed record RequestOrganisationDataExportRequest
{
    public Guid CompanyId { get; init; }
}
