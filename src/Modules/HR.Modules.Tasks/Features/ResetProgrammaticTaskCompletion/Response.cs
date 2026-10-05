namespace HR.Modules.Tasks.Features.ResetProgrammaticTaskCompletion;

internal sealed record ResetProgrammaticTaskCompletionResponse(
    Guid OperationId,
    Guid? TaskId,
    bool WasReset,
    Guid? RecoveryActionId = null,
    int ResetCount = 0,
    string? OperationKind = null);
