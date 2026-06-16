namespace HR.Modules.Tasks.Features.GetUnassignedTasks;

internal sealed record GetUnassignedTasksRequest
{
    public Guid CompanyId { get; init; }
}
