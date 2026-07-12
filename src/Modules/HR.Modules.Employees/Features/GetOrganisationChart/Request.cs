using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Features.GetOrganisationChart;

internal sealed record GetOrganisationChartRequest
{
    public Guid CompanyId { get; init; }
    public Guid? DepartmentId { get; init; }
    public Guid? LocationId { get; init; }
    public EmploymentStatus? Status { get; init; }
}
