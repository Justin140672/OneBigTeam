namespace HR.Modules.Tasks.Services;

/// <summary>
/// A programmatic task completion whose business dispatch or confirmation did not succeed.
/// <see cref="IsTerminal"/> failures are permanent and are never retried automatically.
/// </summary>
internal sealed class TaskCompletionDispatchException(string message, bool isTerminal, Exception? inner = null)
    : Exception(message, inner)
{
    public bool IsTerminal { get; } = isTerminal;
}
