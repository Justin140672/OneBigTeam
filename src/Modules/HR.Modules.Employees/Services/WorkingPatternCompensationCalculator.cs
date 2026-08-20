using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Contracts;

namespace HR.Modules.Employees.Services;

/// <summary>
/// Derives an employee's Hours Per Week and FTE purely from their working pattern (working days +
/// hours per day, falling back to the company's standard/default working pattern when the employee
/// has no override) — never from a directly-supplied/imported number. Shared by every path that
/// creates or updates opening compensation for an employee (manual creation, import) so the
/// calculation can never drift between the two.
/// </summary>
internal sealed class WorkingPatternCompensationCalculator(ICompanyWorkingPatternSettingsReader workingPatternReader)
{
    public async Task<(decimal HoursPerWeek, decimal Fte)> CalculateAsync(
        Guid companyId, WorkingDays? workingDaysOverride, decimal? hoursPerDayOverride, CancellationToken cancellationToken)
    {
        var (defaultWorkingDayCount, defaultHoursPerDay) =
            await workingPatternReader.GetDefaultWorkingPatternAsync(companyId, cancellationToken);

        var standardHoursPerWeek = defaultWorkingDayCount * defaultHoursPerDay;

        var employeeWorkingDayCount = workingDaysOverride is null
            ? defaultWorkingDayCount
            : CountDays(workingDaysOverride.Value);

        var employeeHoursPerDay = hoursPerDayOverride ?? defaultHoursPerDay;

        var hoursPerWeek = employeeWorkingDayCount * employeeHoursPerDay;

        var fte = standardHoursPerWeek == 0m
            ? 0m
            : Math.Round(hoursPerWeek / standardHoursPerWeek, 4, MidpointRounding.AwayFromZero);

        return (hoursPerWeek, fte);
    }

    private static int CountDays(WorkingDays workingDays) =>
        Enum.GetValues<WorkingDays>()
            .Where(d => d != WorkingDays.None && workingDays.HasFlag(d))
            .Count();
}
