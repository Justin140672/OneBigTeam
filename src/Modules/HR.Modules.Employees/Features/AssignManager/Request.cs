namespace HR.Modules.Employees.Features.AssignManager;

internal sealed record AssignManagerRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }

    /// <summary>Null to remove the manager assignment.</summary>
    public Guid? ManagerId { get; init; }
}
