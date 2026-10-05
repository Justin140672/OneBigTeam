namespace HR.Modules.Tasks.Features.ResetProgrammaticTaskCompletion;

internal sealed record ResetProgrammaticTaskCompletionRequest
{
    public Guid CompanyId { get; init; }
    public Guid OperationId { get; init; }
    public string Reason { get; init; } = string.Empty;
}
