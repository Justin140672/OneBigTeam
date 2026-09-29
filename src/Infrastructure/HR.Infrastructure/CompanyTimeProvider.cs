using HR.Modules.Companies.Contracts;
using HR.SharedKernel;

namespace HR.Infrastructure;

internal sealed class CompanyTimeProvider(
    ICurrentUser currentUser,
    ICompanyTimeZoneReader companyTimeZoneReader,
    IClockProvider clockProvider) : ICompanyTimeProvider
{
    public DateOnly Today
    {
        get
        {
            if (currentUser.TenantId is null || !Guid.TryParse(currentUser.TenantId, out var companyId))
            {
                throw new InvalidOperationException("No company context could be resolved for the current user.");
            }

            return GetTodaySync(companyId);
        }
    }

    public TimeZoneInfo TimeZone
    {
        get
        {
            if (currentUser.TenantId is null || !Guid.TryParse(currentUser.TenantId, out var companyId))
            {
                throw new InvalidOperationException("No company context could be resolved for the current user.");
            }

            return GetTimeZoneSync(companyId);
        }
    }

    public async Task<DateOnly> GetTodayAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var ianaTimeZoneId = await companyTimeZoneReader.GetTimeZoneAsync(companyId, cancellationToken);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(ianaTimeZoneId);
        var localDateTime = TimeZoneInfo.ConvertTime(clockProvider.UtcNow, timeZone);
        return DateOnly.FromDateTime(localDateTime.DateTime);
    }

    private DateOnly GetTodaySync(Guid companyId)
    {
        var task = Task.Run(() => companyTimeZoneReader.GetTimeZoneAsync(companyId, CancellationToken.None));
        var ianaTimeZoneId = task.GetAwaiter().GetResult();
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(ianaTimeZoneId);
        var localDateTime = TimeZoneInfo.ConvertTime(clockProvider.UtcNow, timeZone);
        return DateOnly.FromDateTime(localDateTime.DateTime);
    }

    private TimeZoneInfo GetTimeZoneSync(Guid companyId)
    {
        var task = Task.Run(() => companyTimeZoneReader.GetTimeZoneAsync(companyId, CancellationToken.None));
        var ianaTimeZoneId = task.GetAwaiter().GetResult();
        return TimeZoneInfo.FindSystemTimeZoneById(ianaTimeZoneId);
    }
}
