namespace HR.Modules.Tasks.Features.AdjudicateTaskCompletionOperation;

internal sealed record AdjudicationEvidenceRequest
{
    public bool NotificationRequired { get; init; }
    public Guid? AssignedEmployeeId { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? PreviousTaskStatus { get; init; }
    public string? TaskTitle { get; init; }
}

internal sealed record AdjudicateTaskCompletionOperationRequest
{
    public Guid CompanyId { get; init; }
    public Guid OperationId { get; init; }
    public string Resolution { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public AdjudicationEvidenceRequest? Evidence { get; init; }
}
