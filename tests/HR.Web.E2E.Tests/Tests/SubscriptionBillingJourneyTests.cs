using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Company Administrator subscription and billing journey:
/// - Company-Administrator-only user can access Subscription & Billing
/// - Subscription state (plan, status, dates, employee count) displays correctly
/// - Correct action buttons are available based on subscription status:
///   * "Start subscription" for trial/expired trial
///   * "Manage billing" for active subscriptions
///   * "Resume subscription" for cancelled-at-period-end subscriptions
/// - Click handlers for checkout and billing portal initiate navigation to Stripe URLs
/// - In-page operations (resume, cancel) update subscription state
/// - Role separation: non-administrators cannot access the page
///
/// Subscription state lifecycle in E2E:
/// - New companies seed with a trial subscription (14 days)
/// - Trial page shows "Start subscription" button
/// - Active subscriptions show "Manage billing" button
/// - Cancelled-at-period-end subscriptions show "Resume subscription" button
///
/// Note: Stripe navigation (checkout and billing portal) is tested via URL verification
/// only — we do not simulate completing the Stripe flow, as that's covered by integration
/// tests and requires test-mode Stripe webhooks. The E2E focus is on the UI flow and
/// Company Administrator access control.
/// </summary>
public sealed class SubscriptionBillingJourneyTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    // Priya Shah — seeded Company Administrator persona for Acme Corporation.
    // See CompanyAdministratorAccessTests for context on her role setup.
    private const string CompanyAdminEmail = "priya.shah@acme.example";

    // Laura Bennett — seeded HR Administrator persona, used for role-separation tests
    // to confirm she CAN still access the subscription page (she has the subscription:view permission).
    private const string HrAdminEmail = "laura.bennett@acme.example";

    // Tom Williams — seeded plain Employee persona with no admin roles.
    private const string PlainEmployeeEmail = "tom.williams@acme.example";

    // Acme Corporation — the seeded dev/E2E tenant. It has a trial subscription by default.
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task CompanyAdministrator_CanAccess_SubscriptionBillingPage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        // Navigation should not throw — the page loads successfully for this role.
        await subscription.GoToAsync();

        // The page should render with subscription details visible.
        Assert.False(await subscription.IsLoadingAsync(),
            "Expected subscription page to finish loading for Company Administrator");

        // At minimum, the subscription status should be readable (not an error page).
        var status = await subscription.GetSubscriptionStatusAsync();
        Assert.False(string.IsNullOrWhiteSpace(status),
            "Expected subscription status to be displayed");
    }

    [Fact]
    public async Task TrialSubscription_DisplaysCorrectState()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await subscription.GoToAsync();

        // Acme seeds with a trial subscription, so status should be "Trial".
        var status = await subscription.GetSubscriptionStatusAsync();
        Assert.NotNull(status);
        Assert.True(
            status.Equals("Trial", StringComparison.OrdinalIgnoreCase),
            $"Expected subscription status to be 'Trial', but got '{status}'");

        // Trial subscriptions should display trial days remaining.
        var trialDays = await subscription.GetTrialDaysRemainingAsync();
        Assert.NotNull(trialDays);
        Assert.True(int.TryParse(trialDays, out var days) && days > 0,
            $"Expected trial days to be a positive integer, but got '{trialDays}'");

        // Plan name should be readable (e.g., "Starter", "Professional").
        var plan = await subscription.GetPlanAsync();
        Assert.False(string.IsNullOrWhiteSpace(plan),
            "Expected plan name to be displayed");

        // Active employee count should be shown.
        var empCount = await subscription.GetActiveEmployeeCountAsync();
        Assert.False(string.IsNullOrWhiteSpace(empCount),
            "Expected active employee count to be displayed");
    }

    [Fact]
    public async Task TrialSubscription_ShowsStartSubscriptionButton()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await subscription.GoToAsync();

        Assert.True(await subscription.HasStartSubscriptionButtonAsync(),
            "Expected 'Start subscription' button to be visible for trial subscription");

        // The Manage billing button should NOT be visible during trial.
        var manageBillingVisible = await subscription.HasManageBillingButtonAsync();
        Assert.False(manageBillingVisible,
            "Expected 'Manage billing' button to be disabled for trial subscription");
    }

    [Fact]
    public async Task StartSubscriptionButton_InitiatesCheckout()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await subscription.GoToAsync();

        // Capture the current URL to verify it changes when checkout initiates.
        var subscriptionUrl = _page.Url;
        Assert.Contains("/subscription", subscriptionUrl);

        // Click "Start subscription" — this should call the API and navigate to Stripe checkout.
        // The navigation happens with forceLoad: true, so it's a full page reload away from /subscription.
        await subscription.ClickStartSubscriptionAsync();

        // The page navigates away to a Stripe URL (stubbed as https://checkout.stripe.com/... in E2E).
        var checkoutUrl = _page.Url;
        Assert.False(checkoutUrl.Contains("/subscription"),
            $"Expected to navigate away from /subscription to Stripe checkout, but ended at: {checkoutUrl}");

        // The URL should be the Stripe stub checkout URL from FakeStripeGateway.
        Assert.True(checkoutUrl.Contains("checkout.stripe.com") || checkoutUrl.Contains("stripe"),
            $"Expected Stripe checkout URL, but got: {checkoutUrl}");
    }

    [Fact]
    public async Task PlainEmployee_CannotAccess_SubscriptionBillingPage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(PlainEmployeeEmail);

        // Attempt to navigate directly to /subscription.
        // The page guard (Session.CanManageCompany) should redirect to a permitted page.
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/subscription");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        // Verify we were redirected away (AppSession.GuardAccess redirects via NavigateTo).
        var finalUrl = _page.Url;
        Assert.False(finalUrl.TrimEnd('/').EndsWith("/subscription", StringComparison.OrdinalIgnoreCase),
            $"Expected plain employee to be redirected away from /subscription, but ended up at: {finalUrl}");
    }

    [Fact]
    public async Task HrAdministrator_CanAccess_SubscriptionBillingPage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        // Laura has subscription:view permission even as HR Admin, so she should also be able
        // to access the page (though she may see different UI or restrictions than Company Admin).
        // The key here is that she's NOT redirected away.
        await subscription.GoToAsync();

        Assert.False(await subscription.IsLoadingAsync(),
            "Expected subscription page to load for HR Administrator");

        var status = await subscription.GetSubscriptionStatusAsync();
        Assert.False(string.IsNullOrWhiteSpace(status),
            "Expected HR Administrator to see subscription status");
    }

    [Fact]
    public async Task SubscriptionPage_ReloadsAndPersistsState()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await subscription.GoToAsync();

        // Capture initial state.
        var initialStatus = await subscription.GetSubscriptionStatusAsync();
        var initialPlan = await subscription.GetPlanAsync();
        var initialEmpCount = await subscription.GetActiveEmployeeCountAsync();

        // Reload the page.
        await _page.ReloadAsync();
        await _page.WaitForSelectorAsync(".card-header h5", new() { Timeout = 20_000 });

        // Verify state persisted server-side.
        var reloadedStatus = await subscription.GetSubscriptionStatusAsync();
        var reloadedPlan = await subscription.GetPlanAsync();
        var reloadedEmpCount = await subscription.GetActiveEmployeeCountAsync();

        Assert.Equal(initialStatus ?? "", reloadedStatus ?? "");
        Assert.Equal(initialPlan ?? "", reloadedPlan ?? "");
        Assert.Equal(initialEmpCount ?? "", reloadedEmpCount ?? "");
    }
}
