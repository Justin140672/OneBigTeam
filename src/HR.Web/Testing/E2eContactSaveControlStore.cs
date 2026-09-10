#nullable enable
using System.Collections.Concurrent;

namespace HR.Web.Testing;

// TEST-ONLY (E2E_TESTING-gated). Process-wide registry that lets a Playwright E2E test pause,
// release, or fail the server-side outbound "contact details" save that
// EmployeeService.UpdateMyContactDetailsAsync issues to hrapi. Never registered outside E2E.
internal sealed class E2eContactSaveControlStore
{
    private readonly ConcurrentDictionary<string, E2eContactSaveControl> _controls = new();

    // Idempotent get-or-add, keyed by lower-cased employee email.
    internal E2eContactSaveControl Register(string email)
        => _controls.GetOrAdd(email.ToLowerInvariant(), _ => new E2eContactSaveControl());

    internal bool TryGet(string email, out E2eContactSaveControl control)
        => _controls.TryGetValue(email.ToLowerInvariant(), out control!);

    // Never leave an in-flight held request hanging: let it through, then drop the control.
    internal void Remove(string email)
    {
        if (_controls.TryRemove(email.ToLowerInvariant(), out var control))
        {
            control.ReleaseContinue();
        }
    }
}

internal sealed class E2eContactSaveControl
{
    private int _requestCount;
    private readonly TaskCompletionSource<string> _decision =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _arrived =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal int RequestCount => Volatile.Read(ref _requestCount);
    internal bool HasArrived => _arrived.Task.IsCompleted;
    internal bool IsResolved => _decision.Task.IsCompleted;

    internal void ReleaseContinue() => _decision.TrySetResult("continue");
    internal void Fail() => _decision.TrySetResult("fail");

    // Called by the delegating handler when the intercepted request arrives. Blocks the outbound
    // request until the test resolves the decision, bounded to 60s so the HR.Web request thread is
    // never hung (fail-open -> "continue").
    internal async Task<string> OnRequestArrivedAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _requestCount);
        _arrived.TrySetResult(true);

        try
        {
            var completed = await Task.WhenAny(_decision.Task, Task.Delay(TimeSpan.FromSeconds(60), ct))
                .ConfigureAwait(false);
            if (completed == _decision.Task)
            {
                return await _decision.Task.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // fall through to fail-open
        }

        return "continue";
    }
}
