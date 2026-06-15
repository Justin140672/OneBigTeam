namespace HR.Modules.Tasks.Features.ReassignTask;

internal sealed record ReassignTaskRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
    public Guid? AssignedEmployeeId { get; init; }
    public Guid? AssignedUserId { get; init; }

    // Populated by the endpoint from the authenticated user's sub claim.
    internal Guid? ActorUserId { get; init; }
}
