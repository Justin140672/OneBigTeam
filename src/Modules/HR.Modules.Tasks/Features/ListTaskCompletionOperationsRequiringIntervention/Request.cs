namespace HR.Modules.Tasks.Features.ListTaskCompletionOperationsRequiringIntervention;

internal sealed record ListTaskCompletionOperationsRequiringInterventionRequest
{
    public Guid CompanyId { get; init; }
    public string? Status { get; init; }
    public string? FailureCategory { get; init; }
    public Guid? OperationId { get; init; }
    public Guid? TaskId { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
