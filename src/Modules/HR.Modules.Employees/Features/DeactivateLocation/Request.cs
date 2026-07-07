namespace HR.Modules.Employees.Features.DeactivateLocation;

internal sealed record DeactivateLocationRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
}
