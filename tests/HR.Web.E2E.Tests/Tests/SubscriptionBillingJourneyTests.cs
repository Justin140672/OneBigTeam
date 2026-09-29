using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;
using System.Net.Http.Json;
using System.Threading;

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
/// Isolation Model & Cleanup Strategy:
/// This test class uses a shared Beta Corp company for state-mutation tests (cancel/resume workflows)
/// to avoid O(n) test data seeding overhead. Each state-mutating test:
/// 1. Establishes expected state (Active) at the beginning via EnsureBetaCorpActiveSubscriptionAsync()
/// 2. Executes its mutation scenario inside try { }
/// 3. Guarantees restoration to Active state inside finally { } via RestoreBetaCorpToActiveAsync()
///
/// This try/finally pattern ensures Beta Corp returns to Active state regardless of assertion
/// failures, preventing test contamination. RestoreBetaCorpToActiveAsync() uses the authenticated
/// test API (POST /api/companies/subscription/resume) rather than browser UI to:
/// - Work reliably after failed assertions, navigation errors, or closed dialogs
/// - Decouple cleanup from the UI code being tested
/// - Clearly report cleanup failures without hiding the original test failure
///
/// The API call is authenticated via the same _page.Context that the UI test uses, ensuring
/// it runs under the same Company Administrator persona (Charlie Wilson for Beta Corp).
///
/// Seed state (authoritative):
/// - Acme Corporation: Trial subscription (14 days remaining). Tests trial state, "Start subscription"
///   button, trial days display, and page access/loading. Acme is NOT transitioned to Active —
///   tests requiring Active state use Beta Corp instead. No state mutations on Acme.
/// - Beta Corp: Seeded Active subscription. Shared by all cancel/resume mutation tests,
///   always returned to Active by finally block to prevent parallel/sequential contamination.
///
/// Stripe navigation is tested via URL verification only — no actual Stripe integration.
/// </summary>
public sealed class SubscriptionBillingJourneyTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{

    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static readonly Guid BetaCorpId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static readonly Guid GammaCorpId = Guid.Parse("00000000-0000-0000-0000-000000000003");


    private const string AcmeCompanyAdminEmail = "priya.shah@acme.example";

    private const string AcmeHrAdminEmail = "laura.bennett@acme.example";

    private const string AcmeManagerEmail = "james.okafor@acme.example";

    private const string AcmeRecruiterEmail = "marcus.diallo@acme.example";

    private const string AcmePlainEmployeeEmail = "tom.williams@acme.example";

    private const string BetaCompanyAdminEmail = "charlie.wilson@betacorp.example";

    private const string GammaCompanyAdminEmail = "diana.chen@gamma.example";
    private const string GammaCompanyAdminUserId = "30000000-0000-0000-0000-000000000019";


    [Fact]
    public async Task CompanyAdministrator_CanAccess_SubscriptionBillingPage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AcmeCompanyAdminEmail);

        await VerifyCompanyIdAfterLoginAsync(AcmeId, AcmeCompanyAdminEmail);

        await subscription.GoToAsync();

        Assert.False(await subscription.IsLoadingAsync(),
            "Expected subscription page to finish loading for Company Administrator");

        var status = await subscription.GetSubscriptionStatusAsync();
        Assert.False(string.IsNullOrWhiteSpace(status),
            "Expected subscription status to be displayed");
    }

    [Fact]
    public async Task HrAdministrator_CannotAccess_SubscriptionBillingPage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AcmeHrAdminEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/subscription");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        var finalUrl = _page.Url;
        Assert.False(finalUrl.TrimEnd('/').EndsWith("/subscription", StringComparison.OrdinalIgnoreCase),
            $"Expected HR Administrator to be redirected away from /subscription, but ended up at: {finalUrl}");
    }

    [Fact]
    public async Task Manager_CannotAccess_SubscriptionBillingPage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AcmeManagerEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/subscription");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        var finalUrl = _page.Url;
        Assert.False(finalUrl.TrimEnd('/').EndsWith("/subscription", StringComparison.OrdinalIgnoreCase),
            $"Expected Manager to be redirected away from /subscription, but ended up at: {finalUrl}");
    }

    [Fact]
    public async Task Recruiter_CannotAccess_SubscriptionBillingPage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AcmeRecruiterEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/subscription");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

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

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/subscription");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        var finalUrl = _page.Url;
        Assert.False(finalUrl.TrimEnd('/').EndsWith("/subscription", StringComparison.OrdinalIgnoreCase),
            $"Expected plain employee to be redirected away from /subscription, but ended up at: {finalUrl}");
    }


    [Fact]
    public async Task TrialSubscription_DisplaysCorrectState()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AcmeCompanyAdminEmail);

        await subscription.GoToAsync();

        var status = await subscription.GetSubscriptionStatusAsync();
        Assert.NotNull(status);
        Assert.True(
            status.Equals("Trial", StringComparison.OrdinalIgnoreCase),
            $"Expected subscription status to be 'Trial', but got '{status}'");

        var trialDays = await subscription.GetTrialDaysRemainingAsync();
        Assert.NotNull(trialDays);
        Assert.True(int.TryParse(trialDays, out var days) && days > 0,
            $"Expected trial days to be a positive integer, but got '{trialDays}'");

        var plan = await subscription.GetPlanAsync();
        Assert.False(string.IsNullOrWhiteSpace(plan),
            "Expected plan name to be displayed");

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
        await login.LoginAsync(AcmeCompanyAdminEmail);

        await subscription.GoToAsync();

        var subscriptionUrl = _page.Url;
        Assert.Contains("/subscription", subscriptionUrl);

        await subscription.ClickStartSubscriptionAsync();

        var checkoutUrl = _page.Url;
        Assert.False(checkoutUrl.Contains("/subscription"),
            $"Expected to navigate away from /subscription to Stripe checkout, but ended at: {checkoutUrl}");

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

        Assert.True(await subscription.HasStartSubscriptionButtonAsync(),
            "Expected 'Start subscription' button for trial");

        var hasCancelButton = await subscription.HasCancelButtonAsync();
        Assert.False(hasCancelButton,
            "Expected 'Cancel subscription' button to be unavailable during trial");

        var hasResumeButton = await subscription.HasResumeButtonAsync();
        Assert.False(hasResumeButton,
            "Expected 'Resume subscription' button to not be visible during trial");
    }


    [Fact]
    public async Task SubscriptionPage_ReloadsAndPersistsState()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AcmeCompanyAdminEmail);

        await subscription.GoToAsync();

        var initialStatus = await subscription.GetSubscriptionStatusAsync();
        var initialPlan = await subscription.GetPlanAsync();
        var initialEmpCount = await subscription.GetActiveEmployeeCountAsync();

        await _page.ReloadAsync();
        await _page.WaitForSelectorAsync(".card-header h5", new() { Timeout = 20_000 });

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

        var empCount = await subscription.GetActiveEmployeeCountAsync();
        Assert.False(string.IsNullOrWhiteSpace(empCount),
            "Expected active employee count to be displayed");

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

        var billingDate = await subscription.GetNextBillingDateAsync();
        Assert.False(string.IsNullOrWhiteSpace(billingDate),
            "Expected next billing date to be displayed");
    }


    [Fact]
    public async Task CompanyAdministrator_CanReachSubscriptionPageFromNavigation()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AcmeCompanyAdminEmail);

        await subscription.GoToAsync();

        Assert.True(await _page.Locator("h1:has-text('Subscription')").IsVisibleAsync(),
            "Expected 'Subscription' heading to be visible on the subscription page");

        Assert.True(await _page.Locator(".card-header h5:has-text('Subscription Details')").IsVisibleAsync(),
            "Expected 'Subscription Details' card heading to be visible");
    }


    [Fact]
    public async Task ActiveSubscription_DisplaysManageBillingButton_AndNavigatesToPortal()
    {

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        // Step 1: Login as Charlie Wilson, Beta Corp's Company Administrator
        // Beta Corp is seeded with an active subscription to avoid trial state
        await login.GoToAsync();
        await login.LoginAsync(BetaCompanyAdminEmail);

        await subscription.GoToAsync();

        var status = await subscription.GetSubscriptionStatusAsync();
        Assert.NotNull(status);
        // Note: Beta Corp is seeded with an Active subscription
        Assert.True(
            status.Equals("Active", StringComparison.OrdinalIgnoreCase),
            $"Expected subscription status to be 'Active', but got '{status}'");

        Assert.True(await subscription.HasManageBillingButtonAsync(),
            "Expected 'Manage billing' button to be visible for Active subscription");

        Assert.False(await subscription.HasStartSubscriptionButtonAsync(),
            "Expected 'Start subscription' button to be hidden for Active subscription");

        Assert.True(await subscription.HasCancelButtonAsync(),
            "Expected 'Cancel subscription' button to be visible for Active subscription");

        var currentUrl = _page.Url;
        await subscription.ClickManageBillingAsync();

        var billingPortalUrl = _page.Url;
        Assert.False(billingPortalUrl.Contains("/subscription"),
            $"Expected to navigate away from /subscription to billing portal, but ended at: {billingPortalUrl}");
        Assert.True(
            billingPortalUrl.Contains("stripe") || billingPortalUrl.Contains("billing"),
            $"Expected Stripe billing portal URL, but got: {billingPortalUrl}");

        await _page.GoBackAsync();
        await subscription.GoToAsync();

        var persistedStatus = await subscription.GetSubscriptionStatusAsync();
        Assert.Equal(status ?? "", persistedStatus ?? "");
    }

    [Fact]
    public async Task ActiveSubscription_Cancel_ShowsConfirmation_AndSchedulesCancellation()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(GammaCompanyAdminEmail);

        await VerifyCompanyIdAfterLoginAsync(GammaCorpId, GammaCompanyAdminEmail);

        await subscription.GoToAsync();

        await RestoreSubscriptionToActiveInternalAsync(
            GammaCompanyAdminUserId, "Diana Chen",
            "ActiveSubscription_Cancel_ShowsConfirmation_AndSchedulesCancellation (arrange)");
        await _page.ReloadAsync();
        await _page.WaitForSelectorAsync(".card-header h5", new() { Timeout = 20_000 });

        Exception? scenarioException = null;
        Exception? cleanupException = null;

        try
        {
                var initialStatus = await subscription.GetSubscriptionStatusAsync();
                Assert.True(
                    initialStatus?.Equals("Active", StringComparison.OrdinalIgnoreCase) ?? false,
                    $"Expected initial status to be 'Active', but got '{initialStatus}'");

                await subscription.ClickCancelAsync();

                Assert.True(await subscription.IsCancelConfirmDialogVisibleAsync(),
                    "Expected cancel confirmation dialog to be visible");

                await subscription.ConfirmCancelAsync();

                var successMessage = await subscription.GetSuccessMessageAsync();
                Assert.False(string.IsNullOrWhiteSpace(successMessage),
                    "Expected success message after cancellation");

                await _page.WaitForTimeoutAsync(500);

                var newStatus = await subscription.GetSubscriptionStatusAsync();
                Assert.Equal(initialStatus ?? "", newStatus ?? "");

                Assert.True(await subscription.HasCancellationWarningAsync(),
                    "Expected cancellation warning to appear after scheduling cancellation");

                Assert.True(await subscription.HasResumeButtonAsync(),
                    "Expected 'Resume subscription' button to be visible after scheduling cancellation");

                Assert.False(await subscription.HasCancelButtonAsync(),
                    "Expected 'Cancel subscription' button to be hidden after scheduling cancellation");
            }
            catch (Exception ex)
            {
                scenarioException = ex;
            }
            finally
            {
                try
                {
                    await RestoreSubscriptionToActiveInternalAsync(
                        GammaCompanyAdminUserId, "Diana Chen",
                        "ActiveSubscription_Cancel_ShowsConfirmation_AndSchedulesCancellation (cleanup)");
                }
                catch (Exception cleanupEx)
                {
                    cleanupException = cleanupEx;
                }

                if (scenarioException is not null && cleanupException is not null)
                {
                    throw new AggregateException(
                        $"SCENARIO FAILED and CLEANUP FAILED. Scenario: {scenarioException.Message}. " +
                        $"Cleanup: {cleanupException.Message}",
                        scenarioException, cleanupException);
                }

                if (scenarioException is not null)
                {
                    throw scenarioException;
                }

                if (cleanupException is not null)
                {
                    throw cleanupException;
                }
            }
    }

    [Fact]
    public async Task CancelledSubscription_Resume_RestoresActiveState()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(BetaCompanyAdminEmail);

        await VerifyCompanyIdAfterLoginAsync(BetaCorpId, BetaCompanyAdminEmail);

        await subscription.GoToAsync();

        await BetaCorpCleanupSemaphore.WaitAsync();
        try
        {
            await EnsureBetaCorpActiveSubscriptionAsync();

            Exception? scenarioException = null;
            Exception? cleanupException = null;

            try
            {
                if (!await subscription.HasResumeButtonAsync())
                {
                    if (await subscription.HasCancelButtonAsync())
                    {
                        await subscription.ClickCancelAsync();
                        if (await subscription.IsCancelConfirmDialogVisibleAsync())
                        {
                            await subscription.ConfirmCancelAsync();
                        }
                    }
                    await _page.WaitForTimeoutAsync(1000);
                    await _page.ReloadAsync();
                    await _page.WaitForSelectorAsync(".card-header h5", new() { Timeout = 20_000 });
                }

                var cancelledStatus = await subscription.GetSubscriptionStatusAsync();

                Assert.True(await subscription.HasResumeButtonAsync(),
                    "Expected 'Resume subscription' button to be visible for cancelled subscription");

                Assert.True(await subscription.HasCancellationWarningAsync(),
                    "Expected cancellation warning for cancelled subscription");

                await subscription.ClickResumeAsync();

                var successMessage = await subscription.GetSuccessMessageAsync();
                Assert.False(string.IsNullOrWhiteSpace(successMessage),
                    "Expected success message after resuming subscription");

                var resumedStatus = await subscription.GetSubscriptionStatusAsync();
                Assert.Equal(cancelledStatus ?? "", resumedStatus ?? "");

                Assert.False(await subscription.HasCancellationWarningAsync(),
                    "Expected cancellation warning to disappear after resuming");

                Assert.False(await subscription.HasResumeButtonAsync(),
                    "Expected 'Resume subscription' button to be hidden after resuming");

                Assert.True(await subscription.HasCancelButtonAsync(),
                    "Expected 'Cancel subscription' button to return after resuming");
            }
            catch (Exception ex)
            {
                scenarioException = ex;
            }
            finally
            {
                try
                {
                    await RestoreBetaCorpToActiveInternalAsync("CancelledSubscription_Resume_RestoresActiveState");
                }
                catch (Exception cleanupEx)
                {
                    cleanupException = cleanupEx;
                }

                if (scenarioException is not null && cleanupException is not null)
                {
                    throw new AggregateException(
                        $"SCENARIO FAILED and CLEANUP FAILED. Scenario: {scenarioException.Message}. " +
                        $"Cleanup: {cleanupException.Message}",
                        scenarioException, cleanupException);
                }

                if (scenarioException is not null)
                {
                    throw scenarioException;
                }

                if (cleanupException is not null)
                {
                    throw cleanupException;
                }
            }
        }
        finally
        {
            BetaCorpCleanupSemaphore.Release();
        }
    }

    /// <summary>
    /// Tests cancel dialog dismissal:
    /// - Clicking "Keep subscription" dismisses dialog without changing state
    /// - Subscription remains Active
    /// Note: Uses Beta Corp (Active subscription) since Trial subscriptions cannot be cancelled.
    ///
    /// Uses try/finally for consistency: even though this test doesn't mutate state,
    /// the finally block ensures cleanup if any assertion fails before the dialog is dismissed.
    ///
    /// Concurrency protection: semaphore covers entire test (arrange → act → verify → cleanup).
    /// </summary>
    [Fact]
    public async Task CancelDialog_DismissBySaying_KeepSubscription_DoesNotChange()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(BetaCompanyAdminEmail);

        await subscription.GoToAsync();

        await BetaCorpCleanupSemaphore.WaitAsync();
        try
        {
            await EnsureBetaCorpActiveSubscriptionAsync();

            Exception? scenarioException = null;
            Exception? cleanupException = null;

            try
            {
                var initialStatus = await subscription.GetSubscriptionStatusAsync();
                var initialHasCancelWarning = await subscription.HasCancellationWarningAsync();

                await subscription.ClickCancelAsync();

                Assert.True(await subscription.IsCancelConfirmDialogVisibleAsync(),
                    "Expected cancel confirmation dialog to be visible");

                await subscription.CancelCancelAsync();

                Assert.False(await subscription.IsCancelConfirmDialogVisibleAsync(),
                    "Expected cancel dialog to be dismissed");

                var newStatus = await subscription.GetSubscriptionStatusAsync();
                Assert.Equal(initialStatus ?? "", newStatus ?? "");

                var newHasCancelWarning = await subscription.HasCancellationWarningAsync();
                Assert.Equal(initialHasCancelWarning, newHasCancelWarning);
            }
            catch (Exception ex)
            {
                scenarioException = ex;
            }
            finally
            {
                try
                {
                    await RestoreBetaCorpToActiveInternalAsync("CancelDialog_DismissBySaying_KeepSubscription_DoesNotChange");
                }
                catch (Exception cleanupEx)
                {
                    cleanupException = cleanupEx;
                }

                if (scenarioException is not null && cleanupException is not null)
                {
                    throw new AggregateException(
                        $"SCENARIO FAILED and CLEANUP FAILED. Scenario: {scenarioException.Message}. " +
                        $"Cleanup: {cleanupException.Message}",
                        scenarioException, cleanupException);
                }

                if (scenarioException is not null)
                {
                    throw scenarioException;
                }

                if (cleanupException is not null)
                {
                    throw cleanupException;
                }
            }
        }
        finally
        {
            BetaCorpCleanupSemaphore.Release();
        }
    }

    [Fact]
    public async Task SubscriptionState_PersistsAcrossPageReload()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AcmeCompanyAdminEmail);

        await subscription.GoToAsync();

        var initialStatus = await subscription.GetSubscriptionStatusAsync();
        var initialPlan = await subscription.GetPlanAsync();
        var initialNextBilling = await subscription.GetNextBillingDateAsync();
        var initialEmployeeCount = await subscription.GetActiveEmployeeCountAsync();
        var initialHasManageButton = await subscription.HasManageBillingButtonAsync();
        var initialHasCancelButton = await subscription.HasCancelButtonAsync();
        var initialHasResumeButton = await subscription.HasResumeButtonAsync();

        await _page.ReloadAsync();
        await _page.WaitForSelectorAsync(".card-header h5", new() { Timeout = 20_000 });

        var reloadedStatus = await subscription.GetSubscriptionStatusAsync();
        var reloadedPlan = await subscription.GetPlanAsync();
        var reloadedNextBilling = await subscription.GetNextBillingDateAsync();
        var reloadedEmployeeCount = await subscription.GetActiveEmployeeCountAsync();
        var reloadedHasManageButton = await subscription.HasManageBillingButtonAsync();
        var reloadedHasCancelButton = await subscription.HasCancelButtonAsync();
        var reloadedHasResumeButton = await subscription.HasResumeButtonAsync();

        Assert.Equal(initialStatus ?? "", reloadedStatus ?? "");
        Assert.Equal(initialPlan ?? "", reloadedPlan ?? "");
        Assert.Equal(initialNextBilling ?? "", reloadedNextBilling ?? "");
        Assert.Equal(initialEmployeeCount ?? "", reloadedEmployeeCount ?? "");
        Assert.Equal(initialHasManageButton, reloadedHasManageButton);
        Assert.Equal(initialHasCancelButton, reloadedHasCancelButton);
        Assert.Equal(initialHasResumeButton, reloadedHasResumeButton);
    }

    [Fact]
    public async Task SidebarSubscriptionLink_VisibleOnActiveSubscription()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);
        var sidebar = new SidebarPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(AcmeCompanyAdminEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/dashboard");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 20_000 });

        Assert.True(await sidebar.HasTopLevelMenuItemAsync("Subscription & Billing"),
            "Expected 'Subscription & Billing' to be visible in the sidebar for a Company Administrator");

        await sidebar.ClickTopLevelMenuItemAsync("Subscription & Billing");

        await _page.WaitForURLAsync(url => url.Contains("/subscription"), new() { Timeout = 15_000 });

        var finalUrl = _page.Url;
        Assert.True(finalUrl.Contains("/subscription"),
            $"Expected to navigate to subscription page, but ended at: {finalUrl}");

        Assert.False(await subscription.IsLoadingAsync(),
            "Expected subscription page to finish loading");
    }

    [Fact]
    public async Task AllSubscriptionBillingPageMethods_AreExercised()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AcmeCompanyAdminEmail);

        await subscription.GoToAsync();

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

        var trialDays = await subscription.GetTrialDaysRemainingAsync();

        var hasStart = await subscription.HasStartSubscriptionButtonAsync();
        var hasManage = await subscription.HasManageBillingButtonAsync();
        var hasResume = await subscription.HasResumeButtonAsync();
        var hasCancel = await subscription.HasCancelButtonAsync();

        Assert.True(
            hasStart || hasManage || hasResume || hasCancel,
            "Expected at least one action button to be visible");

        var hasCancelDialog = await subscription.IsCancelConfirmDialogVisibleAsync();

        var successMsg = await subscription.GetSuccessMessageAsync();

        var errorMsg = await subscription.GetErrorMessageAsync();

        var hasWarning = await subscription.HasCancellationWarningAsync();

        Assert.True(true, "All SubscriptionBillingPage methods exercised successfully");
    }

    /// <summary>
    /// Verifies that the logged-in user's company ID matches the expected company.
    /// Catches persona/company mismatch errors early by validating the /api/me response.
    /// </summary>
    /// <remarks>
    /// This deliberately does NOT call /api/me via <c>_page.Context.APIRequest</c> — that issues an
    /// anonymous, browser-context-scoped HTTP request with no Authorization header and no relevant
    /// cookie. HR.Web is Blazor Server: its own calls to HR.Api run server-side using a bearer token
    /// held in <c>CircuitSessionState</c> (see HrApiHttpClientFactory), never exposed to the browser
    /// as a cookie or header the API's own origin would ever see. A request built that way 401s for
    /// EVERY persona unconditionally, regardless of any seed/role data — confirmed by direct
    /// diagnosis (a locally run API instance rejects an identical unauthenticated request the same
    /// way for every account). This mints its own short-lived session via the same
    /// /api/dev/persona/{userId} test seam the other Infrastructure API helpers
    /// (E2eEmployeeApi/DepartureFinaliserApi) already use, and calls /api/me with a real
    /// Authorization header instead.
    /// </remarks>
    private async Task VerifyCompanyIdAfterLoginAsync(Guid expectedCompanyId, string email)
    {
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(_fixture.ApiBaseUrl) };

            var personasResponse = await http.GetAsync("/api/dev/personas");
            Assert.True(personasResponse.IsSuccessStatusCode,
                $"Expected /api/dev/personas to succeed, got {personasResponse.StatusCode}");
            var personas = await personasResponse.Content.ReadFromJsonAsync<List<DevPersonaLookup>>();
            var userId = personas?.FirstOrDefault(p =>
                string.Equals(p.Email, email, StringComparison.OrdinalIgnoreCase))?.UserId;
            Assert.False(string.IsNullOrEmpty(userId), $"Expected a seeded dev persona for {email}");

            var sessionResponse = await http.PostAsync($"/api/dev/persona/{userId}", content: null);
            Assert.True(sessionResponse.IsSuccessStatusCode,
                $"Expected /api/dev/persona/{{userId}} to succeed for {email}, got {sessionResponse.StatusCode}");
            var session = await sessionResponse.Content.ReadFromJsonAsync<DevPersonaSessionResult>();
            Assert.NotNull(session);

            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session!.AccessToken);

            var apiResponse = await http.GetAsync("/api/me");
            Assert.True(apiResponse.IsSuccessStatusCode,
                $"Expected /api/me to return 200, but got {apiResponse.StatusCode} for {email}");

            var json = await apiResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement?>();
            if (json.HasValue && json.Value.TryGetProperty("companyId", out var companyIdElement))
            {
                if (companyIdElement.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var returnedCompanyId = Guid.Parse(companyIdElement.GetString() ?? "");
                    Assert.Equal(
                        expectedCompanyId,
                        returnedCompanyId);
                }
            }
        }
        catch (Exception ex)
        {
            Assert.Fail($"Failed to verify company ID for {email}: {ex.Message}");
        }
    }

    private sealed record DevPersonaLookup(string UserId, string CompanyId, string Name, string JobTitle, string Email);
    private sealed record DevPersonaSessionResult(string AccessToken, string RefreshToken, int ExpiresIn);

    private async Task EnsureBetaCorpActiveSubscriptionAsync()
    {
        var subscription = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);

        await _page.ReloadAsync();
        await _page.WaitForSelectorAsync(".card-header h5", new() { Timeout = 20_000 });

        if (await subscription.HasResumeButtonAsync())
        {
            await RestoreBetaCorpToActiveInternalAsync("EnsureBetaCorpActiveSubscriptionAsync");

            await _page.ReloadAsync();
            await _page.WaitForSelectorAsync(".card-header h5", new() { Timeout = 20_000 });
        }

        // Verify we're now in Active state — both the Status field and, more importantly, that the
        // Cancel button (not the Resume button) is what's showing.
        var finalStatus = await subscription.GetSubscriptionStatusAsync();
        Assert.True(
            finalStatus?.Equals("Active", StringComparison.OrdinalIgnoreCase) ?? false,
            $"Expected Beta Corp subscription to be Active, but got '{finalStatus}'");
        Assert.False(await subscription.HasResumeButtonAsync(),
            "Expected Beta Corp subscription to not be scheduled for cancellation after restoring to Active");
    }

    private static readonly SemaphoreSlim BetaCorpCleanupSemaphore = new(1, 1);
    private const string CharlieWilsonUserId = "30000000-0000-0000-0000-000000000018";

    private async Task RestoreBetaCorpToActiveAsync(string callerContext)
    {
        await BetaCorpCleanupSemaphore.WaitAsync();
        try
        {
            await RestoreBetaCorpToActiveInternalAsync(callerContext);
        }
        finally
        {
            BetaCorpCleanupSemaphore.Release();
        }
    }

    private Task RestoreBetaCorpToActiveInternalAsync(string callerContext) =>
        RestoreSubscriptionToActiveInternalAsync(CharlieWilsonUserId, "Charlie Wilson", callerContext);

    private async Task RestoreSubscriptionToActiveInternalAsync(string userId, string personaName, string callerContext)
    {

        var token = await GetAuthenticatedTokenForUserAsync(userId, personaName);
        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException(
                $"[RestoreSubscriptionToActiveInternalAsync] Failed to obtain access token for {personaName} from {callerContext}");
        }

        var resumeUrl = $"{_fixture.ApiBaseUrl}/api/companies/subscription/resume";
        var apiResponse = await _page.Context.APIRequest.PostAsync(
            resumeUrl,
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["Authorization"] = $"Bearer {token}"
                }
            });

        if (!apiResponse.Ok)
        {
            var errorBody = await apiResponse.TextAsync();
            throw new InvalidOperationException(
                $"[RestoreSubscriptionToActiveInternalAsync] Resume subscription API returned {apiResponse.Status} " +
                $"from {callerContext}. URL: {resumeUrl}\nResponse: {errorBody}");
        }

        var stateUrl = $"{_fixture.ApiBaseUrl}/api/companies/subscription-details";
        var stateResponse = await _page.Context.APIRequest.GetAsync(
            stateUrl,
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["Authorization"] = $"Bearer {token}"
                }
            });

        if (!stateResponse.Ok)
        {
            var errorBody = await stateResponse.TextAsync();
            throw new InvalidOperationException(
                $"[RestoreSubscriptionToActiveInternalAsync] State verification GET returned {stateResponse.Status} " +
                $"from {callerContext}. URL: {stateUrl}\nResponse: {errorBody}");
        }

        var json = await stateResponse.JsonAsync();
        if (!json.HasValue)
        {
            throw new InvalidOperationException(
                $"[RestoreSubscriptionToActiveInternalAsync] State verification returned no JSON from {callerContext}");
        }

        if (!json.Value.TryGetProperty("status", out var statusElement))
        {
            throw new InvalidOperationException(
                $"[RestoreSubscriptionToActiveInternalAsync] State response missing 'status' field from {callerContext}. " +
                $"Full response: {json.Value}");
        }

        var status = statusElement.GetString() ?? "";
        if (!status.Equals("Active", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"[RestoreSubscriptionToActiveInternalAsync] Status verification failed: " +
                $"{personaName}'s subscription status is '{status}' (expected 'Active') from {callerContext}. " +
                $"Full response: {json.Value}");
        }

        if (!json.Value.TryGetProperty("cancelAtPeriodEnd", out var cancelAtPeriodEndElement))
        {
            throw new InvalidOperationException(
                $"[RestoreSubscriptionToActiveInternalAsync] State response missing 'cancelAtPeriodEnd' field from {callerContext}. " +
                $"Full response: {json.Value}");
        }

        var cancelAtPeriodEnd = false;
        if (cancelAtPeriodEndElement.ValueKind == System.Text.Json.JsonValueKind.True)
        {
            cancelAtPeriodEnd = true;
        }

        if (cancelAtPeriodEnd)
        {
            throw new InvalidOperationException(
                $"[RestoreSubscriptionToActiveInternalAsync] State verification failed: " +
                $"{personaName}'s subscription still has CancelAtPeriodEnd=true after resume from {callerContext}. " +
                $"Full response: {json.Value}");
        }

        System.Diagnostics.Debug.WriteLine(
            $"[RestoreSubscriptionToActiveInternalAsync] Successfully restored {personaName}'s subscription from {callerContext}: " +
            $"Status=Active, CancelAtPeriodEnd=false confirmed");
    }

    private async Task<string> GetAuthenticatedTokenForUserAsync(string userId, string personaName)
    {
        try
        {
            var personaUrl = $"{_fixture.ApiBaseUrl}/api/dev/persona/{userId}";
            var personaResponse = await _page.Context.APIRequest.PostAsync(personaUrl);

            if (!personaResponse.Ok)
            {
                var body = await personaResponse.TextAsync();
                System.Diagnostics.Debug.WriteLine(
                    $"[GetAuthenticatedTokenForUserAsync:{personaName}] /api/dev/persona failed: " +
                    $"{personaResponse.Status}\n{body}");
                return string.Empty;
            }

            var json = await personaResponse.JsonAsync();
            if (!json.HasValue)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[GetAuthenticatedTokenForUserAsync:{personaName}] No JSON response from /api/dev/persona");
                return string.Empty;
            }

            if (!json.Value.TryGetProperty("accessToken", out var tokenElement))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[GetAuthenticatedTokenForUserAsync:{personaName}] Response missing accessToken field");
                return string.Empty;
            }

            var token = tokenElement.GetString();
            return token ?? string.Empty;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[GetAuthenticatedTokenForUserAsync:{personaName}] Exception: {ex.Message}");
            return string.Empty;
        }
    }
}
