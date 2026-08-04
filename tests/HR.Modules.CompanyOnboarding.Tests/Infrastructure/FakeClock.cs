using HR.SharedKernel;

namespace HR.Modules.CompanyOnboarding.Tests.Infrastructure;

internal sealed class FakeClock : IClock
{
    public FakeClock(DateTime utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTime UtcNow { get; }
}
