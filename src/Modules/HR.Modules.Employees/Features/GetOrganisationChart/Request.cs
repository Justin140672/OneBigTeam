namespace HR.Modules.Employees.Features.GetOrganisationChart;

internal sealed record GetOrganisationChartRequest
{
    public Guid CompanyId { get; init; }
}
