namespace HR.Marketing.Services;

public interface IMarketingAnalytics
{
    void TrackPricingCalculatorViewed();

    void TrackEmployeeCountChanged(int activeEmployees, decimal estimatedMonthlyCost, string ctaShown);

    void TrackStartFreeTrialClicked(int activeEmployees, decimal estimatedMonthlyCost);

    void TrackContactSalesClicked(int activeEmployees, decimal estimatedMonthlyCost);
}
