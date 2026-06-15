using HR.SharedKernel;

namespace HR.Modules.Tasks.Tests.Infrastructure;

internal sealed class FakeClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; } = utcNow;
}
