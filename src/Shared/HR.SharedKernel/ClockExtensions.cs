namespace HR.SharedKernel;

public static class ClockExtensions
{
    public static DateTimeOffset UtcNowOffset(this IClock clock) =>
        new(DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc));

    public static DateOnly TodayIn(this IClock clock, string? timeZoneId)
    {
        TimeZoneInfo timeZone;
        try
        {
            timeZone = string.IsNullOrWhiteSpace(timeZoneId)
                ? TimeZoneInfo.Utc
                : TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            timeZone = TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            timeZone = TimeZoneInfo.Utc;
        }

        var localNow = TimeZoneInfo.ConvertTime(clock.UtcNowOffset(), timeZone);
        return DateOnly.FromDateTime(localNow.DateTime);
    }
}
