using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;

namespace HR.Modules.Leave.Services;

/// <summary>
/// Public-holiday-within-range warning, shared verbatim across preview and both submission paths
/// (LEAVE-08). Kept as a genuinely shared calculation - not a generic service - since
/// PreviewLeaveRequestHandler, SubmitLeaveRequestHandler and SubmitLeaveRequestDraftHandler had
/// each computed this identically and had drifted: only preview surfaced it in its response.
/// </summary>
internal sealed record ExcludedPublicHoliday(DateOnly Date, string Name);

internal sealed class LeaveWarningCalculator(IPublicHolidayReader publicHolidayReader)
{
    public async Task<IReadOnlyList<ExcludedPublicHoliday>> GetExcludedPublicHolidaysAsync(
        Guid companyId,
        DateOnly startDate,
        DateOnly endDate,
        WorkingPattern workingPattern,
        bool excludePublicHolidaysFromLeave,
        CancellationToken cancellationToken)
    {
        if (!excludePublicHolidaysFromLeave)
            return [];

        var holidays = await publicHolidayReader.GetPublicHolidaysAsync(
            companyId, startDate, endDate, cancellationToken);

        return holidays
            .Where(h => workingPattern.IsWorkingDay(h.Date.DayOfWeek))
            .OrderBy(h => h.Date)
            .Select(h => new ExcludedPublicHoliday(h.Date, h.Name))
            .ToList();
    }
}
