namespace HR.Modules.Tasks.Features.ReassignTask;

internal sealed record ReassignTaskRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
    public Guid? AssignedEmployeeId { get; init; }
    public Guid? AssignedUserId { get; init; }
}
