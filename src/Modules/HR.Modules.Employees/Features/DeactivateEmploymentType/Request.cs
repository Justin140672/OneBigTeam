namespace HR.Modules.Employees.Features.DeactivateEmploymentType;

internal sealed record DeactivateEmploymentTypeRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
}
