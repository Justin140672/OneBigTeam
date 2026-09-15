using HR.SharedKernel;

namespace HR.Modules.Leave.Tests.Infrastructure;

internal sealed class FakeClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; } = utcNow;
}
