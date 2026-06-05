using HR.SharedKernel;

namespace HR.Modules.Companies.Tests.Infrastructure;

internal sealed class FakeClock : IClock
{
    public FakeClock(DateTime utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTime UtcNow { get; }
}