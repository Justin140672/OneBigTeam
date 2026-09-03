using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies HR.Admin.Web's read-only Customer Details page (/customers/{CompanyId}):
/// - An allow-listed platform admin sees company/subscription/pricing/employee/storage/settings
///   data for a known seeded company when navigating directly by URL.
/// - Row-click navigation from the Customer List page lands on the matching customer's details.
/// - The billing/login history panels explain they're not yet available rather than showing
///   fabricated data.
/// - An unknown company id shows the error banner rather than crashing.
/// - Anonymous and non-allow-listed access is rejected.
///
/// This page is explicitly view-only (see CustomerDetails.razor) — there is no create/edit/delete
/// flow to cover here.
/// </summary>
public sealed class CustomerDetailsPageTests(EmployeePersonaFixture fixture) : RoleE2ETestBase<EmployeePersonaFixture>(fixture)
{
    // Acme Corporation — the standing seeded dev/E2E tenant (see CompaniesModule.SeedCompaniesAsync).
    // It always has a persisted CompanySettings row (CreateDefault, guarded by Settings being null)
    // and an already-active CustomerSubscription (ActivateSubscription over StartTrial), so it's a
    // reliable "fully populated" fixture for this read-only page.
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // Seeded platform-admin allow-listed persona — see appsettings.Development.json's
    // "PlatformAdmin:AllowedEmails" and DevPersonaStore. Also a valid dev-login persona (any
    // Development persona email + password "password" satisfies HR.Admin.Web's dev-login stub),
    // but being a valid persona and being platform-admin-authorised are two separate checks — see
    // AdminLoginPage's remarks.
    private const string AllowListedAdminEmail = "priya.shah@acme.example";

    // Seeded plain-Employee persona (no platform-admin allow-list entry) — valid dev-login
    // credentials, but the server-side "PlatformAdmin:AllowedEmails" check should still reject
    // every subsequent API call this persona makes against the Admin Portal.
    private const string NonAllowListedEmail = "tom.williams@acme.example";

    [Fact]
    public async Task DirectUrl_ForSeededCompany_ShowsExpectedDetails()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var details = new CustomerDetailsPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await details.GoToAsync(AcmeId);

        Assert.False(await details.IsErrorBannerVisibleAsync(),
            "Expected the allow-listed admin to see customer details, not the error banner");

        // Company information
        Assert.Equal("Acme Corporation", await details.GetCompanyNameAsync());
        Assert.False(string.IsNullOrWhiteSpace(await details.GetStatusAsync()));

        // Subscription — seeded companies are activated (ActivateSubscription over StartTrial),
        // never left as a bare trial.
        Assert.Equal("Active", await details.GetSubscriptionStatusAsync());

        // Current pricing — an active seeded subscription has a real Stripe-stub price attached,
        // so this must not fall back to "Not applicable".
        var monthlyCharge = await details.GetMonthlyChargeAsync();
        Assert.False(string.IsNullOrWhiteSpace(monthlyCharge));
        Assert.NotEqual("Not applicable", monthlyCharge);

        // Employee counts / storage usage stat cards
        Assert.False(string.IsNullOrWhiteSpace(await details.GetActiveEmployeeCountAsync()));
        Assert.False(string.IsNullOrWhiteSpace(await details.GetTotalEmployeeCountAsync()));
        Assert.False(string.IsNullOrWhiteSpace(await details.GetStorageUsedAsync()));
        Assert.False(string.IsNullOrWhiteSpace(await details.GetFilesStoredAsync()));
    }

    [Fact]
    public async Task CompanySettingsSummary_ForSeededCompanyWithSettings_ShowsValues()
    {
        // Acme always has a persisted CompanySettings row (see class remarks), so this must show
        // the real summary, not the "No settings configured yet" fallback.
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var details = new CustomerDetailsPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await details.GoToAsync(AcmeId);

        Assert.True(await details.HasSettingsConfiguredAsync(),
            "Expected Acme's Company settings section to show a populated summary");
        Assert.False(await details.ShowsNoSettingsConfiguredMessageAsync(),
            "Acme has a persisted CompanySettings row and should not show the empty-state message");

        Assert.False(string.IsNullOrWhiteSpace(await details.GetSettingValueAsync("Time zone")));
        Assert.False(string.IsNullOrWhiteSpace(await details.GetSettingValueAsync("Locale")));
        Assert.False(string.IsNullOrWhiteSpace(await details.GetSettingValueAsync("Working days")));
    }

    [Fact]
    public async Task NotYetAvailablePanels_AreVisible_AndExplainNoData()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var details = new CustomerDetailsPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await details.GoToAsync(AcmeId);

        // Billing History is a real, implemented feature now (GetCustomerBillingHistory), not a
        // placeholder — its panel explains honestly why there's nothing to show (no live Stripe
        // key configured in this dev/test environment) rather than saying "not yet available".
        Assert.True(await details.IsBillingHistoryPanelVisibleAsync());
        var billingText = await details.GetBillingHistoryTextAsync() ?? "";
        Assert.Contains("Stripe", billingText, StringComparison.OrdinalIgnoreCase);

        // Login History genuinely is still an unbuilt placeholder.
        Assert.True(await details.IsLoginHistoryPanelVisibleAsync());
        var loginHistoryText = await details.GetLoginHistoryTextAsync() ?? "";
        Assert.Contains("not yet available", loginHistoryText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RowClick_FromCustomerList_NavigatesToMatchingDetails()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var list = new CustomerListPage(_page, _fixture.AdminWebBaseUrl);
        var details = new CustomerDetailsPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await list.GoToAsync();
        Assert.True(await list.HasCompanyAsync("Acme Corporation"),
            "Expected the seeded Acme Corporation row to appear in the customer list");

        await list.ClickCompanyRowAsync("Acme Corporation");

        Assert.Contains($"/customers/{AcmeId}", _page.Url, StringComparison.OrdinalIgnoreCase);
        await _page.WaitForSelectorAsync(".details-grid, .dashboard-error", new() { Timeout = 15_000 });
        Assert.Equal("Acme Corporation", await details.GetCompanyNameAsync());
    }

    [Fact]
    public async Task UnknownCompanyId_ShowsErrorBanner_NotCrash()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var details = new CustomerDetailsPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await details.GoToAsync(Guid.NewGuid());

        Assert.True(await details.IsErrorBannerVisibleAsync(),
            "Expected the error banner for an unknown company id, not a crash or blank page");
        var text = await details.GetErrorBannerTextAsync() ?? "";
        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Fact]
    public async Task NonAllowListedPersona_SeesErrorBanner_NotCustomerData()
    {
        // tom.williams@acme.example is a valid seeded dev-login persona (dev-login stub only
        // checks the persona exists + password "password"), but is not on
        // "PlatformAdmin:AllowedEmails" — the server-side check that actually gates every Admin
        // Portal API call. CustomerDetailsService.GetCustomerDetailsOrNullAsync returns null for
        // any non-2xx response (401 here), which CustomerDetails.razor renders as the same
        // dashboard-error banner used for "not found" — see class remarks.
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var details = new CustomerDetailsPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(NonAllowListedEmail);

        await details.GoToAsync(AcmeId);

        Assert.True(await details.IsErrorBannerVisibleAsync(),
            "Expected a non-allow-listed persona to see the unauthorised error banner");
        Assert.False(await details.HasSettingsConfiguredAsync(),
            "No customer detail sections should render for a non-allow-listed persona");
    }

    [Fact]
    public async Task LoginAsCustomer_ButtonAndNote_AreVisible()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var details = new CustomerDetailsPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await details.GoToAsync(AcmeId);

        Assert.True(await details.LoginAsCustomerButton.WaitUntilVisibleAsync(),
            "Expected the 'Login as customer' button to be visible for an allow-listed platform admin");

        // Honest-limitation messaging must actually render, not be silently dropped.
        Assert.True(await details.IsAutomaticSignInNotYetImplementedNoteVisibleAsync(),
            "Expected the 'full automatic sign-in not yet implemented' note near the button");
    }

    [Fact]
    public async Task LoginAsCustomer_ClickingButton_OpensConfirmDialogRequiringReason()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var details = new CustomerDetailsPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await details.GoToAsync(AcmeId);

        await details.ClickLoginAsCustomerAsync();

        Assert.True(await details.IsLoginAsCustomerDialogVisibleAsync(),
            "Expected the Login as customer confirmation dialog to open");
    }

    [Fact]
    public async Task LoginAsCustomer_SubmittingWithoutReason_IsBlockedWithValidationError()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var details = new CustomerDetailsPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await details.GoToAsync(AcmeId);

        await details.ClickLoginAsCustomerAsync();
        await details.ClickLoginAsCustomerConfirmAsync();

        Assert.True(await details.IsLoginAsCustomerDialogVisibleAsync(),
            "Dialog should remain open when no reason is provided");
        var validationText = await details.GetLoginAsCustomerDialogValidationErrorAsync() ?? "";
        Assert.Contains("reason", validationText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginAsCustomer_SubmittingWithReason_ShowsSuccessPanelWithToken()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var details = new CustomerDetailsPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await details.GoToAsync(AcmeId);

        await details.ClickLoginAsCustomerAsync();
        await details.FillLoginAsCustomerReasonAsync("Investigating a customer-reported compensation export bug");
        await details.ClickLoginAsCustomerConfirmAsync();

        await _page.WaitForSelectorAsync(".admin-action-success", new() { Timeout = 15_000 });

        Assert.True(await details.IsSupportSessionSuccessVisibleAsync(),
            "Expected the success panel to appear after generating a support session");
        var successText = await details.GetSupportSessionSuccessTextAsync() ?? "";
        Assert.Contains("Redemption token", successText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnonymousAccess_RedirectsToLogin()
    {
        // No login at all — unlike an authenticated-but-not-allow-listed caller (rejected
        // server-side, surfaced as this page's own dashboard-error banner), a genuinely
        // anonymous visitor never gets that far: Routes.razor's AuthorizeRouteView redirects to
        // /login at the router level before CustomerDetails.razor — or its API call — ever runs.
        // Navigate directly rather than via CustomerDetailsPage.GoToAsync, which waits for that
        // page's own settled-state selectors (.details-grid/.dashboard-error) and would time out
        // here since neither exists on /login.
        await _page.GotoAsync($"{_fixture.AdminWebBaseUrl}/customers/{AcmeId}");

        await _page.WaitForURLAsync(url => url.ToString().Contains("/login"), new() { Timeout = 20_000 });
    }
}
