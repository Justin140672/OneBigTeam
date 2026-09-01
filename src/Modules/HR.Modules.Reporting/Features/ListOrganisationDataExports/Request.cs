namespace HR.Modules.Reporting.Features.ListOrganisationDataExports;

internal sealed record ListOrganisationDataExportsRequest
{
    public Guid CompanyId { get; init; }
}
