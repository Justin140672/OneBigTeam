namespace HR.Web.Services;

public enum CompensationAdjustmentMode
{
    PercentageIncrease,
    FixedAmountIncrease,
    SetDirectly
}

/// <summary>
/// Pure calculation logic for the bulk compensation adjustment preview grid — deliberately kept
/// free of any Blazor/Razor dependency so it can be unit tested directly (this codebase skips
/// bUnit component tests; Playwright E2E covers the UI itself, so any logic worth testing on its
/// own needs to live outside the .razor file).
/// </summary>
public static class CompensationAdjustmentCalculator
{
    public static decimal CalculateProposedSalary(decimal currentSalary, CompensationAdjustmentMode mode, decimal value)
    {
        return mode switch
        {
            CompensationAdjustmentMode.PercentageIncrease => Round(currentSalary * (1 + value / 100m)),
            CompensationAdjustmentMode.FixedAmountIncrease => Round(currentSalary + value),
            CompensationAdjustmentMode.SetDirectly => Round(value),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported adjustment mode.")
        };
    }

    public static decimal CalculateDifference(decimal currentSalary, decimal proposedSalary) =>
        Round(proposedSalary - currentSalary);

    public static decimal? CalculatePercentageChange(decimal currentSalary, decimal proposedSalary)
    {
        if (currentSalary == 0)
            return null;

        return Round((proposedSalary - currentSalary) / currentSalary * 100m);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
