namespace HR.Modules.Tasks.Features.ResetProgrammaticTaskCompletion;

internal sealed record ResetProgrammaticTaskCompletionResponse(
    Guid OperationId,
    Guid? TaskId,
    bool WasReset);
