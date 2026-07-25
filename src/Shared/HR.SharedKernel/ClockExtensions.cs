namespace HR.SharedKernel;

public static class ClockExtensions
{
    public static DateTimeOffset UtcNowOffset(this IClock clock) =>
        new(DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc));

    /// <summary>
    /// Resolves "today" as a <see cref="DateOnly"/> in the given IANA/Windows time zone id, rather
    /// than assuming UTC. Falls back to UTC if the time zone id is missing or unrecognised by the
    /// host OS. Centralises the pure UTC-instant to local-calendar-day conversion used across
    /// modules for date-boundary/eligibility logic (e.g. leaving dates, probation due dates,
    /// document expiry) so the same BST-transition-safe behaviour is applied everywhere.
    /// </summary>
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
