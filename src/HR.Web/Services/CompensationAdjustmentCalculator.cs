namespace HR.Web.Services;

/// <summary>
/// The three ways HR can express a bulk compensation adjustment in the bulk-update screen's
/// preview grid. The API itself only ever receives the final calculated ProposedSalary per
/// employee — this enum and the calculator below exist purely to drive the UI preview.
/// </summary>
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
    /// <summary>
    /// Calculates the proposed salary for a single employee given the current salary, the
    /// adjustment mode, and the mode-specific input value. For SetDirectly, <paramref name="value"/>
    /// is itself the proposed salary.
    /// </summary>
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

    /// <summary>
    /// Returns null when the current salary is zero (percentage change is undefined) rather than
    /// dividing by zero.
    /// </summary>
    public static decimal? CalculatePercentageChange(decimal currentSalary, decimal proposedSalary)
    {
        if (currentSalary == 0)
            return null;

        return Round((proposedSalary - currentSalary) / currentSalary * 100m);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
