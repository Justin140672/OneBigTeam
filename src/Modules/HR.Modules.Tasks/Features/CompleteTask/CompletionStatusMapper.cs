using HR.Modules.Tasks.Domain;
using HR.SharedKernel;

namespace HR.Modules.Tasks.Features.CompleteTask;

internal static class CompletionStatusMapper
{
    public const string EffectsConfirmed = "confirmed";
    public const string EffectsPending = "pending";
    public const string EffectsVerified = "verified";
    public const string EffectsWaived = "waived";

    public const string ResolutionVerified = "effects_verified";
    public const string ResolutionWaived = "waived";

    public static Result<CompleteTaskResponse> Map(CompleteTaskResponse body, TaskCompletionOperation operation)
    {
        if (operation.IsTerminalFailure)
            return Result.Failure<CompleteTaskResponse>(TerminalConflict(operation));

        return Result.Success(operation.Status switch
        {
            TaskCompletionOperation.StatusProcessed =>
                body with { EffectsStatus = EffectsConfirmed, ResolutionType = null },
            TaskCompletionOperation.StatusEffectsVerified =>
                body with { EffectsStatus = EffectsVerified, ResolutionType = ResolutionVerified },
            TaskCompletionOperation.StatusWaived =>
                body with { EffectsStatus = EffectsWaived, ResolutionType = ResolutionWaived },
            _ => body with { EffectsStatus = EffectsPending, ResolutionType = null },
        });
    }

    public const string CommandMismatchCode = "conflict.task_completion_command_mismatch";

    public static Error CommandMismatch(TaskCompletionOperation operation) => new(
        CommandMismatchCode,
        "Another completion decision already owns or completed this task. Refresh to see the current state.",
        new Dictionary<string, object?>
        {
            ["taskId"] = operation.TaskId,
            ["operationId"] = operation.Id,
            ["status"] = operation.Status,
            ["refresh"] = true,
        });

    public static Error TerminalConflict(TaskCompletionOperation operation)
    {
        var integrity = operation.Status == TaskCompletionOperation.StatusDataIntegrityFailure;

        return new Error(
            integrity ? "conflict.data_integrity_failure" : "conflict.effects_terminal_failure",
            integrity
                ? "The task is completed but its completion effects could not be verified and need investigation by an administrator."
                : "The task is completed but its completion effects failed permanently and need an administrator to retry them.",
            new Dictionary<string, object?>
            {
                ["operationId"] = operation.Id,
                ["taskId"] = operation.TaskId,
                ["status"] = operation.Status,
                ["failureCategory"] = operation.FailureCategory,
                ["resettable"] = !integrity,
                ["recoveryAction"] = integrity ? "adjudicate" : "reset",
            });
    }
}
