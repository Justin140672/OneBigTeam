using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Services;

namespace HR.Modules.Employees.Tests;

public class ProposedLastWorkingDayCalculatorTests
{
    private static readonly DateOnly Monday = new(2026, 10, 5);
    private static readonly DateOnly Tuesday = Monday.AddDays(1);
    private static readonly DateOnly Wednesday = Monday.AddDays(2);
    private static readonly DateOnly Thursday = Monday.AddDays(3);
    private static readonly DateOnly Friday = Monday.AddDays(4);
    private static readonly DateOnly Saturday = Monday.AddDays(5);
    private static readonly DateOnly Sunday = Monday.AddDays(6);

    private static readonly IReadOnlyCollection<DateOnly> NoHolidays = [];

    private static WorkingPattern Pattern(WorkingDays days) => new(days, 7.5m);

    [Fact]
    public void Calculate_Returns_Friday_When_Leaving_Date_Is_Saturday()
    {
        var result = ProposedLastWorkingDayCalculator.Calculate(Saturday, WorkingPattern.Default, NoHolidays);

        Assert.Equal(Friday, result);
    }

    [Fact]
    public void Calculate_Returns_Friday_When_Leaving_Date_Is_Sunday()
    {
        var result = ProposedLastWorkingDayCalculator.Calculate(Sunday, WorkingPattern.Default, NoHolidays);

        Assert.Equal(Friday, result);
    }

    [Fact]
    public void Calculate_Returns_Same_Date_When_Leaving_Date_Is_Working_Day()
    {
        var result = ProposedLastWorkingDayCalculator.Calculate(Wednesday, WorkingPattern.Default, NoHolidays);

        Assert.Equal(Wednesday, result);
    }

    [Fact]
    public void Calculate_Returns_Same_Date_When_Leaving_Date_Is_Monday()
    {
        var result = ProposedLastWorkingDayCalculator.Calculate(Monday, WorkingPattern.Default, NoHolidays);

        Assert.Equal(Monday, result);
    }

    [Fact]
    public void Calculate_Returns_Previous_Working_Day_When_Leaving_Date_Is_Bank_Holiday()
    {
        var result = ProposedLastWorkingDayCalculator.Calculate(Wednesday, WorkingPattern.Default, [Wednesday]);

        Assert.Equal(Tuesday, result);
    }

    [Fact]
    public void Calculate_Returns_Thursday_When_Friday_Is_Bank_Holiday_And_Leaving_Date_Is_Sunday()
    {
        var result = ProposedLastWorkingDayCalculator.Calculate(Sunday, WorkingPattern.Default, [Friday]);

        Assert.Equal(Thursday, result);
    }

    [Fact]
    public void Calculate_Returns_Previous_Friday_When_Monday_And_Tuesday_Are_Bank_Holidays()
    {
        var result = ProposedLastWorkingDayCalculator.Calculate(Tuesday, WorkingPattern.Default, [Monday, Tuesday]);

        Assert.Equal(Monday.AddDays(-3), result);
    }

    [Fact]
    public void Calculate_Ignores_Bank_Holiday_On_Non_Working_Day()
    {
        var result = ProposedLastWorkingDayCalculator.Calculate(Sunday, WorkingPattern.Default, [Saturday, Sunday]);

        Assert.Equal(Friday, result);
    }

    [Fact]
    public void Calculate_Ignores_Bank_Holidays_After_Resolved_Day()
    {
        var result = ProposedLastWorkingDayCalculator.Calculate(Thursday, WorkingPattern.Default, [Friday]);

        Assert.Equal(Thursday, result);
    }

    [Fact]
    public void Calculate_Returns_Wednesday_For_Mon_Tue_Wed_Pattern_When_Leaving_Date_Is_Friday()
    {
        var pattern = Pattern(WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday);

        var result = ProposedLastWorkingDayCalculator.Calculate(Friday, pattern, NoHolidays);

        Assert.Equal(Wednesday, result);
    }

    [Fact]
    public void Calculate_Returns_Thursday_For_Tue_Thu_Pattern_When_Leaving_Date_Is_Sunday()
    {
        var pattern = Pattern(WorkingDays.Tuesday | WorkingDays.Thursday);

        var result = ProposedLastWorkingDayCalculator.Calculate(Sunday, pattern, NoHolidays);

        Assert.Equal(Thursday, result);
    }

    [Fact]
    public void Calculate_Returns_Tuesday_For_Tue_Thu_Pattern_When_Thursday_Is_Bank_Holiday()
    {
        var pattern = Pattern(WorkingDays.Tuesday | WorkingDays.Thursday);

        var result = ProposedLastWorkingDayCalculator.Calculate(Sunday, pattern, [Thursday]);

        Assert.Equal(Tuesday, result);
    }

    [Fact]
    public void Calculate_Returns_Saturday_For_Weekend_Pattern_When_Leaving_Date_Is_Saturday()
    {
        var pattern = Pattern(WorkingDays.Saturday | WorkingDays.Sunday);

        var result = ProposedLastWorkingDayCalculator.Calculate(Saturday, pattern, NoHolidays);

        Assert.Equal(Saturday, result);
    }

    [Fact]
    public void Calculate_Returns_Previous_Sunday_For_Weekend_Pattern_When_Leaving_Date_Is_Friday()
    {
        var pattern = Pattern(WorkingDays.Saturday | WorkingDays.Sunday);

        var result = ProposedLastWorkingDayCalculator.Calculate(Friday, pattern, NoHolidays);

        Assert.Equal(Sunday.AddDays(-7), result);
    }

    [Fact]
    public void Calculate_Uses_Default_Mon_Fri_Pattern_When_Working_Days_Is_None()
    {
        var result = ProposedLastWorkingDayCalculator.Calculate(Sunday, Pattern(WorkingDays.None), NoHolidays);

        Assert.Equal(Friday, result);
    }

    [Fact]
    public void Calculate_Returns_Leaving_Date_When_Every_Day_In_Lookback_Is_Bank_Holiday()
    {
        var holidays = Enumerable.Range(0, ProposedLastWorkingDayCalculator.LookbackDays + 1)
            .Select(i => Sunday.AddDays(-i))
            .ToList();

        var result = ProposedLastWorkingDayCalculator.Calculate(Sunday, WorkingPattern.Default, holidays);

        Assert.Equal(Sunday, result);
    }

    [Fact]
    public void Calculate_Finds_Working_Day_At_Exact_Lookback_Boundary()
    {
        var pattern = Pattern(WorkingDays.Monday);
        var leaving = Monday.AddDays(ProposedLastWorkingDayCalculator.LookbackDays);
        var holidays = Enumerable.Range(0, ProposedLastWorkingDayCalculator.LookbackDays)
            .Select(i => leaving.AddDays(-i))
            .ToList();

        var result = ProposedLastWorkingDayCalculator.Calculate(leaving, pattern, holidays);

        Assert.Equal(Monday, result);
    }

    [Fact]
    public void Calculate_Returns_Leaving_Date_When_Working_Day_Is_Beyond_Lookback()
    {
        var pattern = Pattern(WorkingDays.Monday);
        var leaving = Monday.AddDays(ProposedLastWorkingDayCalculator.LookbackDays + 1);
        var holidays = Enumerable.Range(0, ProposedLastWorkingDayCalculator.LookbackDays + 1)
            .Select(i => leaving.AddDays(-i))
            .ToList();

        var result = ProposedLastWorkingDayCalculator.Calculate(leaving, pattern, holidays);

        Assert.Equal(leaving, result);
    }
}
