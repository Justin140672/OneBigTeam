using HR.SharedKernel;

namespace HR.Modules.Identity.Tests.Infrastructure;

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
