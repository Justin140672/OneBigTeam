namespace HR.Modules.Employees.Features.DeactivateLocationType;

internal sealed record DeactivateLocationTypeRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
}
