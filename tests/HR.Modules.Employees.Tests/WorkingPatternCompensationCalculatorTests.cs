using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Tests.Infrastructure;

namespace HR.Modules.Employees.Tests;

public class WorkingPatternCompensationCalculatorTests
{
    [Fact]
    public async Task CalculateAsync_Uses_Company_Default_When_No_Employee_Override()
    {
        var calculator = new WorkingPatternCompensationCalculator(new FakeCompanyWorkingPatternSettingsReader(5, 7.5m));

        var (hoursPerWeek, fte) = await calculator.CalculateAsync(
            Guid.NewGuid(), workingDaysOverride: null, hoursPerDayOverride: null, CancellationToken.None);

        Assert.Equal(37.5m, hoursPerWeek);
        Assert.Equal(1m, fte);
    }

    [Fact]
    public async Task CalculateAsync_Uses_Employee_Override_When_Present()
    {
        var calculator = new WorkingPatternCompensationCalculator(new FakeCompanyWorkingPatternSettingsReader(5, 7.5m));

        var (hoursPerWeek, fte) = await calculator.CalculateAsync(
            Guid.NewGuid(),
            workingDaysOverride: WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday,
            hoursPerDayOverride: null,
            CancellationToken.None);

        Assert.Equal(22.5m, hoursPerWeek);
        Assert.Equal(0.6m, fte);
    }

    [Fact]
    public async Task CalculateAsync_Uses_Employee_HoursPerDay_Override()
    {
        var calculator = new WorkingPatternCompensationCalculator(new FakeCompanyWorkingPatternSettingsReader(5, 7.5m));

        var (hoursPerWeek, fte) = await calculator.CalculateAsync(
            Guid.NewGuid(), workingDaysOverride: null, hoursPerDayOverride: 4m, CancellationToken.None);

        Assert.Equal(20m, hoursPerWeek);
        Assert.Equal(Math.Round(20m / 37.5m, 4), fte);
    }
}
