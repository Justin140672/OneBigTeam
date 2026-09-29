namespace HR.SharedKernel.Pricing;

public sealed record SubscriptionPricingBand(int StartEmployee, int? EndEmployee, decimal PricePerEmployee);

public sealed record SubscriptionPricingConfig(
    IReadOnlyList<SubscriptionPricingBand> Bands,
    decimal MinimumMonthlyChargeGbp)
{
    public static SubscriptionPricingConfig Default { get; } = new(
        new[]
        {
            new SubscriptionPricingBand(1, 50, 2.00m),
            new SubscriptionPricingBand(51, 150, 1.75m),
            new SubscriptionPricingBand(151, null, 1.50m),
        },
        20.00m);

    public Result Validate()
    {
        if (Bands is null || Bands.Count == 0)
        {
            return Result.Failure(Error.Validation("At least one pricing band is required."));
        }

        if (MinimumMonthlyChargeGbp < 0)
        {
            return Result.Failure(Error.Validation("Minimum monthly charge cannot be negative."));
        }

        for (var i = 0; i < Bands.Count; i++)
        {
            var band = Bands[i];
            var isFinal = i == Bands.Count - 1;

            if (band.PricePerEmployee < 0)
            {
                return Result.Failure(Error.Validation("Band price per employee cannot be negative."));
            }

            if (band.StartEmployee < 1)
            {
                return Result.Failure(Error.Validation("Band start must be 1 or greater."));
            }

            if (band.EndEmployee is null && !isFinal)
            {
                return Result.Failure(Error.Validation("Only the final band may be unlimited."));
            }

            if (band.EndEmployee is not null)
            {
                if (band.EndEmployee < 1)
                {
                    return Result.Failure(Error.Validation("Band end must be 1 or greater."));
                }

                if (band.EndEmployee < band.StartEmployee)
                {
                    return Result.Failure(Error.Validation("Band end must be greater than or equal to band start."));
                }
            }

            if (i == 0)
            {
                if (band.StartEmployee != 1)
                {
                    return Result.Failure(Error.Validation("The first band must start at employee 1."));
                }
            }
            else
            {
                var previousEnd = Bands[i - 1].EndEmployee;
                if (previousEnd is null)
                {
                    return Result.Failure(Error.Validation("The unlimited band must be the last band."));
                }

                if (band.StartEmployee != previousEnd.Value + 1)
                {
                    return Result.Failure(Error.Validation(
                        "Pricing bands must be contiguous with no gaps or overlaps."));
                }
            }
        }

        if (Bands[^1].EndEmployee is not null)
        {
            return Result.Failure(Error.Validation("The final band must be unlimited (cover all remaining employees)."));
        }

        return Result.Success();
    }
}

public sealed record SubscriptionPricingBandCharge(
    string BandRangeLabel,
    int StartEmployee,
    int? EndEmployee,
    int EmployeesInBand,
    decimal PricePerEmployee,
    decimal Subtotal);

public sealed record SubscriptionPricingBreakdown(
    int ActiveEmployeeCount,
    IReadOnlyList<SubscriptionPricingBandCharge> BandBreakdown,
    decimal CalculatedEmployeeCharge,
    decimal MinimumMonthlyChargeGbp,
    decimal FinalMonthlyCharge);

public static class SubscriptionPricingCalculator
{
    public static SubscriptionPricingBreakdown Calculate(int activeEmployeeCount, SubscriptionPricingConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var count = Math.Max(0, activeEmployeeCount);
        var bandCharges = new List<SubscriptionPricingBandCharge>(config.Bands.Count);
        decimal calculatedEmployeeCharge = 0m;

        foreach (var band in config.Bands)
        {
            int employeesInBand;
            if (count < band.StartEmployee)
            {
                employeesInBand = 0;
            }
            else if (band.EndEmployee is null)
            {
                employeesInBand = count - band.StartEmployee + 1;
            }
            else
            {
                var upper = Math.Min(count, band.EndEmployee.Value);
                employeesInBand = upper - band.StartEmployee + 1;
            }

            if (employeesInBand < 0)
            {
                employeesInBand = 0;
            }

            var subtotal = employeesInBand * band.PricePerEmployee;
            calculatedEmployeeCharge += subtotal;

            bandCharges.Add(new SubscriptionPricingBandCharge(
                BuildRangeLabel(band),
                band.StartEmployee,
                band.EndEmployee,
                employeesInBand,
                band.PricePerEmployee,
                subtotal));
        }

        var finalMonthlyCharge = Math.Max(calculatedEmployeeCharge, config.MinimumMonthlyChargeGbp);

        return new SubscriptionPricingBreakdown(
            count,
            bandCharges,
            calculatedEmployeeCharge,
            config.MinimumMonthlyChargeGbp,
            finalMonthlyCharge);
    }

    private static string BuildRangeLabel(SubscriptionPricingBand band) =>
        band.EndEmployee is null
            ? $"{band.StartEmployee}+"
            : $"{band.StartEmployee}–{band.EndEmployee.Value}";
}
