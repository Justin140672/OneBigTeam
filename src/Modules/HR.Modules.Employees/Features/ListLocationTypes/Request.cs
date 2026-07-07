namespace HR.Modules.Employees.Features.ListLocationTypes;

internal sealed record ListLocationTypesRequest
{
    public Guid CompanyId { get; init; }
    public bool? IsActive { get; init; }
}
