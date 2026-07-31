using HR.Marketing.Services;

namespace HR.Marketing.Tests;

public class PricingCtaResolverTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(499)]
    [InlineData(500)]
    public void Resolve_AtOrBelowThreshold_ShowsStartFreeTrialAsPrimary(int employees)
    {
        var result = PricingCtaResolver.Resolve(employees, largeOrganisationThreshold: 500);

        Assert.Equal(PricingCtaResolver.StartFreeTrial, result.PrimaryLabel);
        Assert.False(result.IsLargeOrganisation);
    }

    [Theory]
    [InlineData(501)]
    [InlineData(1000)]
    public void Resolve_AboveThreshold_ShowsContactSalesAsPrimary(int employees)
    {
        var result = PricingCtaResolver.Resolve(employees, largeOrganisationThreshold: 500);

        Assert.Equal(PricingCtaResolver.ContactSales, result.PrimaryLabel);
        Assert.Equal(PricingCtaResolver.StartFreeTrial, result.SecondaryLabel);
        Assert.True(result.IsLargeOrganisation);
    }

    [Fact]
    public void Resolve_ThresholdIsConfigurable()
    {
        var belowCustomThreshold = PricingCtaResolver.Resolve(50, largeOrganisationThreshold: 40);
        var atCustomThreshold = PricingCtaResolver.Resolve(40, largeOrganisationThreshold: 40);

        Assert.True(belowCustomThreshold.IsLargeOrganisation);
        Assert.False(atCustomThreshold.IsLargeOrganisation);
    }

    [Fact]
    public void Resolve_AboveThreshold_StillReturnsPricingRelevantSupportingMessage()
    {
        var result = PricingCtaResolver.Resolve(501, largeOrganisationThreshold: 500);

        Assert.DoesNotContain("contact us for pricing", result.SupportingMessage, StringComparison.OrdinalIgnoreCase);
    }
}
