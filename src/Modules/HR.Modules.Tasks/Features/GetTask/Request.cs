namespace HR.Modules.Tasks.Features.GetTask;

internal sealed record GetTaskRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
}
