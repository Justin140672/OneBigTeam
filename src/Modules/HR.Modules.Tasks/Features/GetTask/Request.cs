namespace HR.Modules.Tasks.Features.GetTask;

internal sealed record GetTaskRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }

    // Populated by the endpoint from the authenticated user's resolved employee id (IAM-07).
    internal Guid CallerEmployeeId { get; init; }
}
