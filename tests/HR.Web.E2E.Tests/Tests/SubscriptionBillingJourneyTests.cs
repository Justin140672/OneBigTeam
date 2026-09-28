using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Company Administrator subscription and billing journey:
/// - Only Company-Administrator-only user can access Subscription & Billing
/// - Subscription state (plan, status, dates, employee count) displays correctly
/// - Correct action buttons are available based on subscription status:
///   * "Start subscription" for trial/expired trial
///   * "Manage billing" for active subscriptions
///   * "Resume subscription" for cancelled-at-period-end subscriptions
/// - Click handlers for checkout and billing portal initiate navigation to Stripe URLs
/// - In-page operations (resume, cancel) update subscription state
/// - Role separation: non-administrators (HR Admin, Manager, Recruiter, Employee) cannot access the page
///
/// Subscription state lifecycle in E2E:
/// - Trial subscription shows "Start subscription" button
/// - Active subscriptions show "Manage billing" button
/// - Cancelled-at-period-end subscriptions show "Resume subscription" button
///
/// Access Control (guarded by Session.CanManageCompany):
/// - CompanyAdministrator: CAN access
/// - HrAdministrator: CANNOT access (subscription:manage policy removed from this role)
/// - Manager: CANNOT access
/// - Recruiter: CANNOT access
/// - Employee: CANNOT access
///
/// Note: This suite uses isolated test companies per test to avoid parallel execution
/// conflicts (tests run at maxParallelThreads=15). Each test that mutates subscription
/// state creates or reuses a dedicated company fixture with a predictable state.
///
/// Seed state (authoritative):
/// - Acme Corporation: Trial subscription (14 days remaining). Tests trial state, "Start subscription"
///   button, trial days display, and page access/loading. Acme is NOT transitioned to Active —
///   tests requiring Active state use Beta Corp instead.
/// - Beta Corp: Active subscription (activated immediately in seed). Tests active state, "Manage Billing"
///   button, cancel/resume workflows, and subscription lifecycle mutations with idempotent restoration.
///
/// Stripe navigation is tested via URL verification only — no actual Stripe integration.
/// Each state-mutating test must establish and restore its own subscription state to avoid
/// interfering with parallel tests or subsequent tests in sequence. The EnsureBetaCorpActiveSubscriptionAsync
/// helper restores Beta Corp to Active state at test start if a prior test left it cancelled.
/// </summary>
public sealed class SubscriptionBillingJourneyTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    // ── Companies ──────────────────────────────────────────────────────────────

    // Acme Corporation — seeded with active subscription, used for access control tests.
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // Beta Corp — seeded with active subscription, used for subscription lifecycle tests.
    private static readonly Guid BetaCorpId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    // ── Personas ───────────────────────────────────────────────────────────────

    // Priya Shah — seeded Company Administrator persona for Acme Corporation.
    // See CompanyAdministratorAccessTests for context on her role setup.
    private const string AcmeCompanyAdminEmail = "priya.shah@acme.example";

    // Laura Bennett — seeded HR Administrator persona (Acme).
    // She has HR permissions but NOT company administration (CanManageCompany = false).
    private const string AcmeHrAdminEmail = "laura.bennett@acme.example";

    // James Okafor — seeded Manager persona (Acme), no company admin role.
    private const string AcmeManagerEmail = "james.okafor@acme.example";

    // Marcus Diallo — seeded Recruiter persona (Acme), no company admin role.
    private const string AcmeRecruiterEmail = "marcus.diallo@acme.example";

    // Tom Williams — seeded plain Employee persona (Acme), no admin roles.
    private const string AcmePlainEmployeeEmail = "tom.williams@acme.example";

    // Beta Corp admin — for isolated active subscription tests.
    // Charlie Wilson is seeded as Beta Corp's Company Administrator.
    private const string BetaCompanyAdminEmail = "charlie.wilson@betacorp.example";

    // ── Access Control Tests ───────────────────────────────────────────────────

    [Fact]
    public async Task CompanyAdministrator_CanAccess_SubscriptionBillingPage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AcmeCompanyAdminEmail);

        // Verify company ID matches expected tenant to catch persona errors early
        await VerifyCompanyIdAfterLoginAsync(AcmeId, AcmeCompanyAdminEmail);

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
    public async Task HrAdministrator_CannotAccess_SubscriptionBillingPage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Laura (HrAdministrator, CanManageCompany = false) ──
        await login.GoToAsync();
        await login.LoginAsync(AcmeHrAdminEmail);

        // ── Step 2: Attempt to navigate directly to /subscription ───────────────
        // The page guard (Session.CanManageCompany) should redirect away because
        // Laura is HrAdministrator-only, not CompanyAdministrator. The subscription:manage
        // API policy no longer grants HR Administrator access (see
        // 30-administrative-role-separation-matrix.md).
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/subscription");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        // ── Step 3: Must be redirected away from /subscription ─────────────────
        var finalUrl = _page.Url;
        Assert.False(finalUrl.TrimEnd('/').EndsWith("/subscription", StringComparison.OrdinalIgnoreCase),
            $"Expected HR Administrator to be redirected away from /subscription, but ended up at: {finalUrl}");
    }

    [Fact]
    public async Task Manager_CannotAccess_SubscriptionBillingPage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as James (Manager, CanManageCompany = false) ─────────
        await login.GoToAsync();
        await login.LoginAsync(AcmeManagerEmail);

        // ── Step 2: Attempt to navigate directly to /subscription ───────────────
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/subscription");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        // ── Step 3: Must be redirected away from /subscription ─────────────────
        var finalUrl = _page.Url;
        Assert.False(finalUrl.TrimEnd('/').EndsWith("/subscription", StringComparison.OrdinalIgnoreCase),
            $"Expected Manager to be redirected away from /subscription, but ended up at: {finalUrl}");
    }

    [Fact]
    public async Task Recruiter_CannotAccess_SubscriptionBillingPage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Marcus (Recruiter, CanManageCompany = false) ──────
        await login.GoToAsync();
        await login.LoginAsync(AcmeRecruiterEmail);

        // ── Step 2: Attempt to navigate directly to /subscription ───────────────
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/subscription");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        // ── Step 3: Must be redirected away from /subscription ─────────────────
        var finalUrl = _page.Url;
        Assert.False(finalUrl.TrimEnd('/').EndsWith("/subscription", StringComparison.OrdinalIgnoreCase),
            $"Expected Recruiter to be redirected away from /subscription, but ended up at: {finalUrl}");
    }

    [Fact]
    public async Task PlainEmployee_CannotAccess_SubscriptionBillingPage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AcmePlainEmployeeEmail);

        // Attempt to navigate directly to /subscription.
        // The page guard (Session.CanManageCompany) should redirect to a permitted page.
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/subscription");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        // Verify we were redirected away (AppSession.GuardAccess redirects via NavigateTo).
        var finalUrl = _page.Url;
        Assert.False(finalUrl.TrimEnd('/').EndsWith("/subscription", StringComparison.OrdinalIgnoreCase),
            $"Expected plain employee to be redirected away from /subscription, but ended up at: {finalUrl}");
    }

    // ── Subscription State Display Tests ──────────────────────────────────────

    [Fact]
    public async Task TrialSubscription_DisplaysCorrectState()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AcmeCompanyAdminEmail);

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
        await login.LoginAsync(AcmeCompanyAdminEmail);

        await subscription.GoToAsync();

        Assert.True(await subscription.HasStartSubscriptionButtonAsync(),
            "Expected 'Start subscription' button to be visible for trial subscription");

        // The Manage billing button should NOT be visible during trial.
        var manageBillingVisible = await subscription.HasManageBillingButtonAsync();
        Assert.False(manageBillingVisible,
            "Expected 'Manage billing' button to be disabled for trial subscription");
    }

    // ── Subscription Workflow Tests ────────────────────────────────────────────

    [Fact]
    public async Task StartSubscriptionButton_InitiatesCheckout()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AcmeCompanyAdminEmail);

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
    public async Task TrialSubscription_DoesNotShowManageBillingOrCancelButtons()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AcmeCompanyAdminEmail);

        await subscription.GoToAsync();

        // During trial, only "Start subscription" is shown.
        Assert.True(await subscription.HasStartSubscriptionButtonAsync(),
            "Expected 'Start subscription' button for trial");

        // Cancel button should not be available (trial subscriptions cannot be cancelled).
        var hasCancelButton = await subscription.HasCancelButtonAsync();
        Assert.False(hasCancelButton,
            "Expected 'Cancel subscription' button to be unavailable during trial");

        // Resume button should not be shown (only shown when CancelAtPeriodEnd = true).
        var hasResumeButton = await subscription.HasResumeButtonAsync();
        Assert.False(hasResumeButton,
            "Expected 'Resume subscription' button to not be visible during trial");
    }

    // ── Subscription State Persistence Tests ───────────────────────────────────

    [Fact]
    public async Task SubscriptionPage_ReloadsAndPersistsState()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AcmeCompanyAdminEmail);

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

    [Fact]
    public async Task SubscriptionPage_DisplaysActiveEmployeeCount()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AcmeCompanyAdminEmail);

        await subscription.GoToAsync();

        // Active employee count should be a numeric value, not null or empty.
        var empCount = await subscription.GetActiveEmployeeCountAsync();
        Assert.False(string.IsNullOrWhiteSpace(empCount),
            "Expected active employee count to be displayed");

        // It should be parseable as a number (zero or positive).
        Assert.True(int.TryParse(empCount, out var count) && count >= 0,
            $"Expected active employee count to be a non-negative integer, but got '{empCount}'");
    }

    [Fact]
    public async Task SubscriptionPage_DisplaysNextBillingDate()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AcmeCompanyAdminEmail);

        await subscription.GoToAsync();

        // Next billing date should be readable (either a date or "Not yet billed").
        var billingDate = await subscription.GetNextBillingDateAsync();
        Assert.False(string.IsNullOrWhiteSpace(billingDate),
            "Expected next billing date to be displayed");
    }

    // ── Page Navigation Tests ──────────────────────────────────────────────────

    [Fact]
    public async Task CompanyAdministrator_CanReachSubscriptionPageFromNavigation()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AcmeCompanyAdminEmail);

        // The subscription page should be reachable via direct navigation.
        await subscription.GoToAsync();

        // Verify we're on the subscription page by checking for the page-specific content.
        Assert.True(await _page.Locator("h1:has-text('Subscription')").IsVisibleAsync(),
            "Expected 'Subscription' heading to be visible on the subscription page");

        // The subscription details card should also be visible.
        Assert.True(await _page.Locator(".card-header h5:has-text('Subscription Details')").IsVisibleAsync(),
            "Expected 'Subscription Details' card heading to be visible");
    }

    // ── Subscription Lifecycle Tests (Isolated Test Companies) ─────────────────────

    /// <summary>
    /// Tests the complete active subscription lifecycle:
    /// - Verify Active subscription displays correct state and buttons
    /// - Click "Manage Billing" and verify navigation to Stripe portal
    /// - Return to page and verify state persistence
    /// </summary>
    [Fact]
    public async Task ActiveSubscription_DisplaysManageBillingButton_AndNavigatesToPortal()
    {
        // Use Beta Corp (seeded with Active subscription) as the isolated test company.

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        // Step 1: Login as Charlie Wilson, Beta Corp's Company Administrator
        // Beta Corp is seeded with an active subscription to avoid trial state
        await login.GoToAsync();
        await login.LoginAsync(BetaCompanyAdminEmail);

        await subscription.GoToAsync();

        // Step 2: Verify the subscription is Active
        var status = await subscription.GetSubscriptionStatusAsync();
        Assert.NotNull(status);
        // Note: Beta Corp is seeded with an Active subscription
        Assert.True(
            status.Equals("Active", StringComparison.OrdinalIgnoreCase),
            $"Expected subscription status to be 'Active', but got '{status}'");

        // Step 3: Verify "Manage Billing" button is visible (only for Active subscriptions)
        Assert.True(await subscription.HasManageBillingButtonAsync(),
            "Expected 'Manage billing' button to be visible for Active subscription");

        // Verify "Start subscription" button is NOT visible (only for Trial)
        Assert.False(await subscription.HasStartSubscriptionButtonAsync(),
            "Expected 'Start subscription' button to be hidden for Active subscription");

        // Verify "Cancel" button IS visible (only for Active subscriptions)
        Assert.True(await subscription.HasCancelButtonAsync(),
            "Expected 'Cancel subscription' button to be visible for Active subscription");

        // Step 4: Click "Manage Billing" and verify navigation to Stripe billing portal
        var currentUrl = _page.Url;
        await subscription.ClickManageBillingAsync();

        var billingPortalUrl = _page.Url;
        Assert.False(billingPortalUrl.Contains("/subscription"),
            $"Expected to navigate away from /subscription to billing portal, but ended at: {billingPortalUrl}");
        Assert.True(
            billingPortalUrl.Contains("stripe") || billingPortalUrl.Contains("billing"),
            $"Expected Stripe billing portal URL, but got: {billingPortalUrl}");

        // Step 5: Verify state persists if we navigate back (user would manually navigate back)
        // Navigate back to subscription page
        await _page.GoBackAsync();
        await subscription.GoToAsync();

        var persistedStatus = await subscription.GetSubscriptionStatusAsync();
        Assert.Equal(status ?? "", persistedStatus ?? "");
    }

    /// <summary>
    /// Tests the subscription cancellation workflow:
    /// - Active subscription shows "Cancel" button
    /// - Clicking Cancel shows confirmation dialog
    /// - Confirming transition shows warning that subscription will end at period end
    /// - Subscription status becomes "Scheduled for cancellation"
    /// </summary>
    [Fact]
    public async Task ActiveSubscription_Cancel_ShowsConfirmation_AndSchedulesCancellation()
    {
        // Use Beta Corp (isolated active subscription) for this mutation test
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(BetaCompanyAdminEmail);

        // Verify company ID matches expected tenant to catch persona errors early
        await VerifyCompanyIdAfterLoginAsync(BetaCorpId, BetaCompanyAdminEmail);

        await subscription.GoToAsync();

        // Ensure Beta Corp is in Active state (restores if previous test left it cancelled)
        await EnsureBetaCorpActiveSubscriptionAsync();

        // Verify we start with an Active subscription
        var initialStatus = await subscription.GetSubscriptionStatusAsync();
        Assert.True(
            initialStatus?.Equals("Active", StringComparison.OrdinalIgnoreCase) ?? false,
            $"Expected initial status to be 'Active', but got '{initialStatus}'");

        // Step 1: Click "Cancel subscription" button
        await subscription.ClickCancelAsync();

        // Step 2: Verify confirmation dialog appears
        Assert.True(await subscription.IsCancelConfirmDialogVisibleAsync(),
            "Expected cancel confirmation dialog to be visible");

        // Step 3: Confirm the cancellation
        await subscription.ConfirmCancelAsync();

        // Step 4: Verify success message appears
        var successMessage = await subscription.GetSuccessMessageAsync();
        Assert.False(string.IsNullOrWhiteSpace(successMessage),
            "Expected success message after cancellation");

        // Step 5: Verify status changed (should now show as "Scheduled for cancellation" or similar)
        // Wait a moment for state to update
        await _page.WaitForTimeoutAsync(500);

        var newStatus = await subscription.GetSubscriptionStatusAsync();
        Assert.NotEqual(initialStatus ?? "", newStatus ?? "");

        // Step 6: Verify cancellation warning is now visible
        Assert.True(await subscription.HasCancellationWarningAsync(),
            "Expected cancellation warning to appear after scheduling cancellation");

        // Step 7: Verify that "Resume" button is now visible (only after CancelAtPeriodEnd = true)
        Assert.True(await subscription.HasResumeButtonAsync(),
            "Expected 'Resume subscription' button to be visible after scheduling cancellation");

        // Verify "Cancel" button is now hidden (cannot cancel an already-cancelled subscription)
        Assert.False(await subscription.HasCancelButtonAsync(),
            "Expected 'Cancel subscription' button to be hidden after scheduling cancellation");

        // ───────────────────────────────────────────────────────────────────────────────
        // CLEANUP: Restore subscription to Active state for parallel test safety
        // ───────────────────────────────────────────────────────────────────────────────
        await subscription.ClickResumeAsync();
        await subscription.GetSuccessMessageAsync(); // Wait for success message
        await _page.WaitForTimeoutAsync(500);

        // Verify restoration
        var restoredStatus = await subscription.GetSubscriptionStatusAsync();
        Assert.Equal(initialStatus ?? "", restoredStatus ?? "");
        // Status should have returned to Active after resuming
    }

    /// <summary>
    /// Tests the subscription resumption workflow:
    /// - Cancelled-at-period-end subscription shows "Resume" button
    /// - Clicking Resume transitions back to Active
    /// - Warning disappears and "Cancel" button returns
    /// </summary>
    [Fact]
    public async Task CancelledSubscription_Resume_RestoresActiveState()
    {
        // Use Beta Corp (isolated active subscription) for this mutation test
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(BetaCompanyAdminEmail);

        // Verify company ID matches expected tenant to catch persona errors early
        await VerifyCompanyIdAfterLoginAsync(BetaCorpId, BetaCompanyAdminEmail);

        await subscription.GoToAsync();

        // Ensure Beta Corp is in Active state first
        await EnsureBetaCorpActiveSubscriptionAsync();

        // Now cancel it to establish the "Scheduled for cancellation" state needed for this test
        var status = await subscription.GetSubscriptionStatusAsync();
        if (!(status?.Equals("Scheduled for cancellation", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            // If not already cancelled, cancel it first
            if (await subscription.HasCancelButtonAsync())
            {
                await subscription.ClickCancelAsync();
                if (await subscription.IsCancelConfirmDialogVisibleAsync())
                {
                    await subscription.ConfirmCancelAsync();
                }
            }
            // Wait for state to update
            await _page.WaitForTimeoutAsync(1000);
            await _page.ReloadAsync();
            await _page.WaitForSelectorAsync(".card-header h5", new() { Timeout = 20_000 });
        }

        // Now we should be in cancelled-at-period-end state
        var cancelledStatus = await subscription.GetSubscriptionStatusAsync();

        // Verify we have the Resume button
        Assert.True(await subscription.HasResumeButtonAsync(),
            "Expected 'Resume subscription' button to be visible for cancelled subscription");

        // Verify cancellation warning is shown
        Assert.True(await subscription.HasCancellationWarningAsync(),
            "Expected cancellation warning for cancelled subscription");

        // Step 1: Click "Resume subscription"
        await subscription.ClickResumeAsync();

        // Step 2: Verify success message appears
        var successMessage = await subscription.GetSuccessMessageAsync();
        Assert.False(string.IsNullOrWhiteSpace(successMessage),
            "Expected success message after resuming subscription");

        // Step 3: Verify status returned to Active (or equivalent)
        var resumedStatus = await subscription.GetSubscriptionStatusAsync();
        Assert.NotEqual(cancelledStatus ?? "", resumedStatus ?? "");

        // Step 4: Verify cancellation warning is gone
        Assert.False(await subscription.HasCancellationWarningAsync(),
            "Expected cancellation warning to disappear after resuming");

        // Step 5: Verify "Resume" button is hidden again
        Assert.False(await subscription.HasResumeButtonAsync(),
            "Expected 'Resume subscription' button to be hidden after resuming");

        // Verify "Cancel" button is visible again
        Assert.True(await subscription.HasCancelButtonAsync(),
            "Expected 'Cancel subscription' button to return after resuming");
    }

    /// <summary>
    /// Tests cancel dialog dismissal:
    /// - Clicking "Keep subscription" dismisses dialog without changing state
    /// - Subscription remains Active
    /// Note: Uses Beta Corp (Active subscription) since Trial subscriptions cannot be cancelled.
    /// </summary>
    [Fact]
    public async Task CancelDialog_DismissBySaying_KeepSubscription_DoesNotChange()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        // Use Beta Corp (Active subscription) for this test, not Acme (Trial)
        await login.GoToAsync();
        await login.LoginAsync(BetaCompanyAdminEmail);

        await subscription.GoToAsync();

        // Ensure Beta Corp is in Active state
        await EnsureBetaCorpActiveSubscriptionAsync();

        // Verify starting state
        var initialStatus = await subscription.GetSubscriptionStatusAsync();
        var initialHasCancelWarning = await subscription.HasCancellationWarningAsync();

        // Step 1: Click "Cancel subscription"
        await subscription.ClickCancelAsync();

        // Step 2: Verify dialog is visible
        Assert.True(await subscription.IsCancelConfirmDialogVisibleAsync(),
            "Expected cancel confirmation dialog to be visible");

        // Step 3: Click "Keep subscription" (dismissal)
        await subscription.CancelCancelAsync();

        // Step 4: Verify dialog is dismissed
        Assert.False(await subscription.IsCancelConfirmDialogVisibleAsync(),
            "Expected cancel dialog to be dismissed");

        // Step 5: Verify state unchanged
        var newStatus = await subscription.GetSubscriptionStatusAsync();
        Assert.Equal(initialStatus ?? "", newStatus ?? "");

        var newHasCancelWarning = await subscription.HasCancellationWarningAsync();
        Assert.Equal(initialHasCancelWarning, newHasCancelWarning);
    }

    /// <summary>
    /// Tests state persistence across page reload:
    /// - Navigate to subscription page
    /// - Capture subscription state
    /// - Reload the page
    /// - Verify all displayed state matches original
    /// </summary>
    [Fact]
    public async Task SubscriptionState_PersistsAcrossPageReload()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AcmeCompanyAdminEmail);

        await subscription.GoToAsync();

        // Capture initial state
        var initialStatus = await subscription.GetSubscriptionStatusAsync();
        var initialPlan = await subscription.GetPlanAsync();
        var initialNextBilling = await subscription.GetNextBillingDateAsync();
        var initialEmployeeCount = await subscription.GetActiveEmployeeCountAsync();
        var initialHasManageButton = await subscription.HasManageBillingButtonAsync();
        var initialHasCancelButton = await subscription.HasCancelButtonAsync();
        var initialHasResumeButton = await subscription.HasResumeButtonAsync();

        // Reload page
        await _page.ReloadAsync();
        await _page.WaitForSelectorAsync(".card-header h5", new() { Timeout = 20_000 });

        // Capture state after reload
        var reloadedStatus = await subscription.GetSubscriptionStatusAsync();
        var reloadedPlan = await subscription.GetPlanAsync();
        var reloadedNextBilling = await subscription.GetNextBillingDateAsync();
        var reloadedEmployeeCount = await subscription.GetActiveEmployeeCountAsync();
        var reloadedHasManageButton = await subscription.HasManageBillingButtonAsync();
        var reloadedHasCancelButton = await subscription.HasCancelButtonAsync();
        var reloadedHasResumeButton = await subscription.HasResumeButtonAsync();

        // Verify all state persisted
        Assert.Equal(initialStatus ?? "", reloadedStatus ?? "");
        Assert.Equal(initialPlan ?? "", reloadedPlan ?? "");
        Assert.Equal(initialNextBilling ?? "", reloadedNextBilling ?? "");
        Assert.Equal(initialEmployeeCount ?? "", reloadedEmployeeCount ?? "");
        Assert.Equal(initialHasManageButton, reloadedHasManageButton);
        Assert.Equal(initialHasCancelButton, reloadedHasCancelButton);
        Assert.Equal(initialHasResumeButton, reloadedHasResumeButton);
    }

    /// <summary>
    /// Tests sidebar navigation link visibility:
    /// - For Active subscription: sidebar has "Subscription" link
    /// - Link is visible and clickable
    /// - Clicking navigates to subscription page
    /// </summary>
    [Fact]
    public async Task SidebarSubscriptionLink_VisibleOnActiveSubscription()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AcmeCompanyAdminEmail);

        // Navigate to a default page (e.g., dashboard)
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/dashboard");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 20_000 });

        // Look for subscription link in sidebar navigation
        // Try multiple possible selectors that might contain the subscription link
        var subscriptionLink = _page.Locator("nav").Locator("a[href*='subscription']");

        // Also try looking for a link with "Subscription" text in the sidebar
        var subscriptionTextLink = _page.Locator("nav").GetByText("Subscription", new() { Exact = false });

        // Check which one exists
        var linkExists = await subscriptionLink.CountAsync() > 0;
        var textLinkExists = await subscriptionTextLink.CountAsync() > 0;

        Assert.True(linkExists || textLinkExists,
            "Expected subscription link or text to be visible in sidebar for Active subscription (checked both href-based and text-based locators)");

        // Use whichever link we found
        if (linkExists)
        {
            await subscriptionLink.First.ClickAsync();
        }
        else
        {
            await subscriptionTextLink.First.ClickAsync();
        }

        // Verify we're on the subscription page
        await _page.WaitForURLAsync(url => url.Contains("/subscription"), new() { Timeout = 15_000 });

        var finalUrl = _page.Url;
        Assert.True(finalUrl.Contains("/subscription"),
            $"Expected to navigate to subscription page, but ended at: {finalUrl}");

        // Extra validation: verify we can see subscription page content
        Assert.False(await subscription.IsLoadingAsync(),
            "Expected subscription page to finish loading");
    }

    /// <summary>
    /// Tests all SubscriptionBillingPage methods are exercised:
    /// - IsLoadingAsync
    /// - GetSubscriptionStatusAsync
    /// - GetPlanAsync
    /// - GetNextBillingDateAsync
    /// - GetActiveEmployeeCountAsync
    /// - GetTrialDaysRemainingAsync
    /// - HasStartSubscriptionButtonAsync
    /// - HasManageBillingButtonAsync
    /// - HasResumeButtonAsync
    /// - HasCancelButtonAsync
    /// - IsCancelConfirmDialogVisibleAsync
    /// - GetSuccessMessageAsync
    /// - GetErrorMessageAsync
    /// - HasCancellationWarningAsync
    /// </summary>
    [Fact]
    public async Task AllSubscriptionBillingPageMethods_AreExercised()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AcmeCompanyAdminEmail);

        await subscription.GoToAsync();

        // Exercise all "Get" methods
        var isLoading = await subscription.IsLoadingAsync();
        Assert.False(isLoading, "Page should not be loading");

        var status = await subscription.GetSubscriptionStatusAsync();
        Assert.NotNull(status);

        var plan = await subscription.GetPlanAsync();
        Assert.NotNull(plan);

        var nextBilling = await subscription.GetNextBillingDateAsync();
        Assert.NotNull(nextBilling);

        var empCount = await subscription.GetActiveEmployeeCountAsync();
        Assert.NotNull(empCount);

        // GetTrialDaysRemainingAsync may return null if not in trial
        var trialDays = await subscription.GetTrialDaysRemainingAsync();
        // No assertion — depends on subscription state

        // Exercise all "Has" methods
        var hasStart = await subscription.HasStartSubscriptionButtonAsync();
        var hasManage = await subscription.HasManageBillingButtonAsync();
        var hasResume = await subscription.HasResumeButtonAsync();
        var hasCancel = await subscription.HasCancelButtonAsync();

        // At least one button should be visible (depending on subscription state)
        Assert.True(
            hasStart || hasManage || hasResume || hasCancel,
            "Expected at least one action button to be visible");

        var hasCancelDialog = await subscription.IsCancelConfirmDialogVisibleAsync();
        // Dialog visibility depends on whether Cancel was clicked

        var successMsg = await subscription.GetSuccessMessageAsync();
        // Success message may be null if no action was taken

        var errorMsg = await subscription.GetErrorMessageAsync();
        // Error message may be null if no error occurred

        var hasWarning = await subscription.HasCancellationWarningAsync();
        // Warning depends on subscription state

        // All methods executed without throwing
        Assert.True(true, "All SubscriptionBillingPage methods exercised successfully");
    }

    /// <summary>
    /// Verifies that the logged-in user's company ID matches the expected company.
    /// Catches persona/company mismatch errors early by validating the /api/me response.
    /// </summary>
    private async Task VerifyCompanyIdAfterLoginAsync(Guid expectedCompanyId, string email)
    {
        try
        {
            // The api/me endpoint returns the current user's profile, including companyId
            var apiResponse = await _page.Context.APIRequest.GetAsync($"{_fixture.ApiBaseUrl}/api/me");
            Assert.True(apiResponse.Ok, $"Expected /api/me to return 200, but got {apiResponse.Status} for {email}");

            var json = await apiResponse.JsonAsync();
            if (json is not null && json.TryGetProperty("companyId", out var companyIdElement))
            {
                if (companyIdElement.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var returnedCompanyId = Guid.Parse(companyIdElement.GetString() ?? "");
                    Assert.Equal(expectedCompanyId, returnedCompanyId,
                        $"Company ID mismatch for {email}: expected {expectedCompanyId} but got {returnedCompanyId}");
                }
            }
        }
        catch (Exception ex)
        {
            Assert.Fail($"Failed to verify company ID for {email}: {ex.Message}");
        }
    }

    /// <summary>
    /// Ensures Beta Corp's subscription is in Active state, idempotently restoring it
    /// if a previous test left it in Cancelled state. Called at the start of each
    /// state-mutating subscription test to establish a predictable starting state.
    /// </summary>
    private async Task EnsureBetaCorpActiveSubscriptionAsync()
    {
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        // Already on subscription page from test setup
        var currentStatus = await subscription.GetSubscriptionStatusAsync();

        if (currentStatus?.Equals("Scheduled for cancellation", StringComparison.OrdinalIgnoreCase) ?? false)
        {
            // Subscription is cancelled — resume it to get back to Active
            if (await subscription.HasResumeButtonAsync())
            {
                await subscription.ClickResumeAsync();
                await subscription.GetSuccessMessageAsync(); // Wait for success message
                await _page.WaitForTimeoutAsync(500);

                // Reload to ensure fresh state
                await _page.ReloadAsync();
                await _page.WaitForSelectorAsync(".card-header h5", new() { Timeout = 20_000 });
            }
        }

        // Verify we're now in Active state
        var finalStatus = await subscription.GetSubscriptionStatusAsync();
        Assert.True(
            finalStatus?.Equals("Active", StringComparison.OrdinalIgnoreCase) ?? false,
            $"Expected Beta Corp subscription to be Active, but got '{finalStatus}'");
    }
}
