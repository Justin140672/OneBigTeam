using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Employees.Services;

internal static class CompanyToday
{
    public static async Task<DateOnly> ResolveAsync(
        Guid companyId,
        IClock clock,
        ICompanyTimeZoneReader timeZoneReader,
        CancellationToken cancellationToken)
    {
        var timeZoneId = await timeZoneReader.GetTimeZoneAsync(companyId, cancellationToken);
        return clock.TodayIn(timeZoneId);
    }
}
