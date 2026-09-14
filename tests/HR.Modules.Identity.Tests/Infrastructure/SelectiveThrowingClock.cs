using HR.SharedKernel;

namespace HR.Modules.Identity.Tests.Infrastructure;

/// <summary>
/// Throws on exactly the Nth access to <see cref="UtcNow"/> (1-indexed, counted from construction)
/// and returns <paramref name="baseTime"/> on every other access. Used to simulate a genuine
/// mid-operation failure inside AccountDisablementJob's try block (before the row is marked
/// Processed/saved) — the only realistically injectable failure point for a job with no other
/// externally-replaceable collaborator besides IClock/IAuditEventPublisher/the DbContext itself.
/// </summary>
internal sealed class SelectiveThrowingClock(DateTime baseTime, int throwOnCallNumber) : IClock
{
    private int _callCount;

    public DateTime UtcNow
    {
        get
        {
            _callCount++;
            if (_callCount == throwOnCallNumber)
                throw new InvalidOperationException("Simulated clock failure.");

            return baseTime;
        }
    }
}
