namespace HR.Marketing.Services;

public sealed class LoggingMarketingAnalytics(ILogger<LoggingMarketingAnalytics> logger) : IMarketingAnalytics
{
    public void TrackPricingCalculatorViewed() =>
        logger.LogInformation("Analytics event: {Event}", "Pricing Calculator Viewed");

    public void TrackEmployeeCountChanged(int activeEmployees, decimal estimatedMonthlyCost, string ctaShown) =>
        logger.LogInformation(
            "Analytics event: {Event} Employees={ActiveEmployees} MonthlyCost={MonthlyCost} Cta={CtaShown}",
            "Employee Count Changed", activeEmployees, estimatedMonthlyCost, ctaShown);

    public void TrackStartFreeTrialClicked(int activeEmployees, decimal estimatedMonthlyCost) =>
        logger.LogInformation(
            "Analytics event: {Event} Employees={ActiveEmployees} MonthlyCost={MonthlyCost}",
            "Start Free Trial Clicked", activeEmployees, estimatedMonthlyCost);

    public void TrackContactSalesClicked(int activeEmployees, decimal estimatedMonthlyCost) =>
        logger.LogInformation(
            "Analytics event: {Event} Employees={ActiveEmployees} MonthlyCost={MonthlyCost}",
            "Contact Sales Clicked", activeEmployees, estimatedMonthlyCost);
}
