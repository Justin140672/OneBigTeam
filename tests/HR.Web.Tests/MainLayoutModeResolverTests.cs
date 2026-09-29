using HR.Web.Services;

namespace HR.Web.Tests;

public class MainLayoutModeResolverTests
{
    [Theory]
    [InlineData(false, SubscriptionResolution.Unresolved, false, true, MainLayoutMode.Loading)]
    [InlineData(false, SubscriptionResolution.Resolved, false, true, MainLayoutMode.Loading)]
    [InlineData(true, SubscriptionResolution.Unresolved, false, true, MainLayoutMode.Loading)]
    [InlineData(true, SubscriptionResolution.Resolved, false, true, MainLayoutMode.CompleteProfile)]
    [InlineData(true, SubscriptionResolution.Resolved, true, true, MainLayoutMode.Shell)]
    [InlineData(true, SubscriptionResolution.Resolved, true, false, MainLayoutMode.Shell)]
    [InlineData(true, SubscriptionResolution.Resolved, false, false, MainLayoutMode.Shell)]
    [InlineData(true, SubscriptionResolution.Failed, false, true, MainLayoutMode.Shell)]
    [InlineData(true, SubscriptionResolution.Failed, false, false, MainLayoutMode.Shell)]
    public void Resolve_Returns_Expected_Mode(
        bool isLoaded, SubscriptionResolution resolution, bool isReadOnly, bool requiresSetup, MainLayoutMode expected)
    {
        Assert.Equal(expected, MainLayoutModeResolver.Resolve(isLoaded, resolution, isReadOnly, requiresSetup));
    }
}
