using HR.SharedKernel;

namespace HR.Modules.Leave.Tests.Infrastructure;

/// <summary>
/// Throws on exactly the Nth access to <see cref="UtcNow"/> (1-indexed, counted from construction)
/// and returns <paramref name="baseTime"/> on every other access. Mirrors
/// HR.Modules.Identity.Tests.Infrastructure.SelectiveThrowingClock — used to simulate a genuine
/// mid-operation failure inside LeavePolicyDeactivationJob's try block.
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
