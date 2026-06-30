namespace HR.Modules.Employees.Features.ListEmploymentTypes;

internal sealed record ListEmploymentTypesRequest
{
    public Guid CompanyId { get; init; }
    public bool? IsActive { get; init; }
}
