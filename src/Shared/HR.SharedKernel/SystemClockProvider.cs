namespace HR.SharedKernel;

public sealed class SystemClockProvider : IClockProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
