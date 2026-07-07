namespace HR.Modules.Employees.Features.GetLocation;

internal sealed record GetLocationRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
}
