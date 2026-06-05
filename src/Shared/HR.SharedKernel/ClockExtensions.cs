namespace HR.SharedKernel;

public static class ClockExtensions
{
    public static DateTimeOffset UtcNowOffset(this IClock clock) =>
        new(DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc));
}
