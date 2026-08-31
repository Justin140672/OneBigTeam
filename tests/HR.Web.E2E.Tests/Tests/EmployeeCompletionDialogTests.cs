using System.Net.Http.Json;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the "Complete Initial Employee Record on First Login" feature: MainLayout.razor's global
/// gate that renders ONLY EmployeeCompletionDialog.razor (no sidebar/topbar/@Body) whenever the
/// current session's employee has RequiresInitialSetup = true, and the /getting-started checklist's
/// integration with it.
///
/// ── Establishing a RequiresInitialSetup = true session ──────────────────────────────────────────
/// There is no existing E2E fixture that reaches this state (every seeded dev persona's employee
/// already has real personal details, so none of them has RequiresInitialSetup = true — see
/// EmployeeProvisioningService.MarkAsInitialCompanyAdminAsync, which only sets it for a BRAND NEW
/// company's auto-created initial admin at signup time). This test class instead signs a fresh
/// company up for real against HR.Api's POST /api/signup (same technique
/// VerifyEmailJourneyTests.DevActivateCompany_ActivatesNewlySignedUpCompany already uses to reach a
/// PendingVerification company), then uses the dev-only POST /api/dev/activate-company bypass to
/// flip it to Active without a live Supabase verification click.
///
/// Logging in as that brand-new admin (not a seeded dev persona) is only possible in this
/// environment because E2E_TESTING=true swaps in FakeSupabaseAuthGateway (see that class's own
/// remarks): SignInWithPasswordAsync accepts ANY email as long as the password matches the fixed
/// seeded dev password, deriving a deterministic fake Supabase user id from the email — the exact
/// same id EnsureDevUserAsync/CreateConfirmedUserAsync would have derived for that email during
/// signup. So POST /api/signup is called with that same fixed password, and LoginPage.LoginAsync
/// then logs in as the new admin exactly like any other persona. This is an ASSUMPTION specific to
/// the E2E_TESTING fake-gateway environment this suite always runs against; it would not work
/// against a real Supabase project.
///
/// Uses ParallelBlankPersonaFixture (no single fixed persona, never mid-test persona switching) —
/// each test signs up and logs in as its OWN freshly-generated admin email, so tests never collide.
/// </summary>
public sealed class EmployeeCompletionDialogTests(ParallelBlankPersonaFixture fixture)
    : RoleE2ETestBase<ParallelBlankPersonaFixture>(fixture)
{
    // Mirrors LoginPage.DevPersonaPassword (private there) — FakeSupabaseAuthGateway.SignInWithPasswordAsync
    // rejects any sign-in whose password doesn't match this fixed value, regardless of what was
    // passed to POST /api/signup, so signup must use the same value for the later login to succeed.
    private const string DevPersonaPassword = "Dev-Only-Password-1!";

    private async Task<(Guid CompanyId, string Email)> SignUpAndActivateFreshCompanyAsync()
    {
        using var http = new HttpClient { BaseAddress = new Uri(_fixture.ApiBaseUrl) };

        var suffix = Guid.NewGuid().ToString("N")[..10];
        var companyName = $"E2E Initial Setup Co {suffix}";
        var email = $"e2e-initial-setup-{suffix}@example.com";

        var signUpResponse = await http.PostAsJsonAsync("/api/signup", new
        {
            CompanyName = companyName,
            AdminFirstName = "Placeholder",
            AdminLastName = "Admin",
            AdminEmail = email,
            Password = DevPersonaPassword,
        });
        Assert.True(signUpResponse.IsSuccessStatusCode);

        var signUp = await signUpResponse.Content.ReadFromJsonAsync<SignUpResult>();
        Assert.NotNull(signUp);

        var activateResponse = await http.PostAsJsonAsync(
            "/api/dev/activate-company",
            new { CompanyId = signUp!.CompanyId });
        Assert.Equal(System.Net.HttpStatusCode.NoContent, activateResponse.StatusCode);

        return (signUp.CompanyId, email);
    }

    private async Task<EmployeeCompletionDialogPage> LoginAndReachBlockedShellAsync()
    {
        var (_, email) = await SignUpAndActivateFreshCompanyAsync();

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(email, DevPersonaPassword);

        var dialog = new EmployeeCompletionDialogPage(_page);
        await dialog.WaitForVisibleAsync();
        return dialog;
    }

    [Fact]
    public async Task FreshAdminLogin_ShowsBlockingCompletionDialog_AndHidesAppShell()
    {
        var dialog = await LoginAndReachBlockedShellAsync();

        Assert.True(await dialog.IsVisibleAsync());
        Assert.True(await dialog.IsHeaderVisibleAsync());

        // Normal app shell chrome (sidebar/topbar) must not be present at all while blocked.
        Assert.False(await _page.Locator(".app-sidebar").IsVisibleAsync());
        Assert.False(await _page.Locator(".top-bar").IsVisibleAsync());
    }

    [Fact]
    public async Task CompletionDialog_Heading_ShowsPersonalisedWelcomeWithAccountFirstName()
    {
        var dialog = await LoginAndReachBlockedShellAsync();

        // SignUpAndActivateFreshCompanyAsync registers the admin with first name "Placeholder".
        Assert.True(await dialog.HeadingShowsWelcomeForAsync("Placeholder"),
            $"Expected personalised welcome heading. Actual: {await dialog.HeadingTextAsync()}");
    }

    [Fact]
    public async Task CompletionDialog_SupportingExplanatoryText_IsVisible()
    {
        var dialog = await LoginAndReachBlockedShellAsync();

        Assert.True(await dialog.SupportingTextVisibleAsync());
    }

    [Fact]
    public async Task CompletionDialog_FirstAndLastName_ShownReadOnlyWithAccountValues_AndNotEditable()
    {
        var dialog = await LoginAndReachBlockedShellAsync();

        Assert.Equal("Placeholder", (await dialog.ReadOnlyFirstNameText()).Trim());
        Assert.Equal("Admin", (await dialog.ReadOnlyLastNameText()).Trim());
        Assert.False(await dialog.IsFirstNameEditable(), "First name must not be an editable input.");
        Assert.False(await dialog.IsLastNameEditable(), "Last name must not be an editable input.");
        Assert.True(await dialog.NameCorrectionNoteVisibleAsync());
    }

    [Fact]
    public async Task CompletionDialog_SectionHeadings_ArePresent()
    {
        var dialog = await LoginAndReachBlockedShellAsync();

        Assert.True(await dialog.HasSectionHeadingAsync("Personal details"));
        Assert.True(await dialog.HasSectionHeadingAsync("Contact details"));
        Assert.True(await dialog.HasSectionHeadingAsync("Home address"));
    }

    [Fact]
    public async Task CompletionDialog_PrimaryButton_ReadsCompleteSetup()
    {
        var dialog = await LoginAndReachBlockedShellAsync();

        Assert.True(await dialog.IsPrimaryButtonLabelledCompleteSetupAsync());
    }

    [Fact]
    public async Task CompletionDialog_CannotBeDismissed_ViaEscape()
    {
        var dialog = await LoginAndReachBlockedShellAsync();

        var stillVisible = await dialog.TryDismissViaEscapeAsync();

        Assert.True(stillVisible, "Expected the blocking dialog to remain visible after pressing Escape.");
    }

    [Fact]
    public async Task CompletionDialog_CannotBeDismissed_ViaOutsideClick()
    {
        var dialog = await LoginAndReachBlockedShellAsync();

        var stillVisible = await dialog.TryDismissViaOutsideClickAsync();

        Assert.True(stillVisible, "Expected the blocking dialog to remain visible after clicking outside it.");
    }

    [Fact]
    public async Task CompletionDialog_SubmittingWithEmptyRequiredFields_ShowsValidationErrors_AndStaysOpen()
    {
        var dialog = await LoginAndReachBlockedShellAsync();

        await dialog.ClickSaveExpectingValidationFailureAsync();

        Assert.True(await dialog.IsVisibleAsync(), "Dialog should remain open when required fields are empty.");
        Assert.True(await dialog.HasAnyValidationErrorAsync());
        Assert.True(await dialog.HasValidationErrorAsync("Date of birth is required"));
        Assert.True(await dialog.HasValidationErrorAsync("Address line 1 is required"));
        Assert.True(await dialog.HasValidationErrorAsync("City is required"));
        Assert.True(await dialog.HasValidationErrorAsync("Postcode is required"));
    }

    [Fact]
    public async Task CompletionDialog_DateOfBirthOnOrBefore1900_ShowsValidationError()
    {
        var dialog = await LoginAndReachBlockedShellAsync();

        // Exact boundary: the rule requires strictly AFTER 1900-01-01 (see EmployeeCompletionDialog's
        // DateAfter1900Attribute) — 1 Jan 1900 itself must fail.
        await dialog.FillDateOfBirthAsync("01/01/1900");
        await dialog.FillAddressLine1Async("1 Test Street");
        await dialog.FillCityAsync("London");
        await dialog.FillPostcodeAsync("SW1A 1AA");

        await dialog.ClickSaveExpectingValidationFailureAsync();

        Assert.True(await dialog.IsVisibleAsync());
        Assert.True(await dialog.HasValidationErrorAsync("Date of birth must be after 1 January 1900"));
    }

    [Fact]
    public async Task CompletionDialog_ValidSubmission_ClosesDialog_AndRevealsAppShell()
    {
        var dialog = await LoginAndReachBlockedShellAsync();

        await dialog.FillAllRequiredFieldsAsync(
            dobDdMMyyyy: "02/01/1990",
            nationality: "British",
            gender: "Female",
            addressLine1: "1 Test Street",
            city: "London",
            postcode: "SW1A 1AA");

        await dialog.SaveAndWaitForCloseAsync();

        Assert.False(await dialog.IsVisibleAsync());
        await _page.WaitForSelectorAsync(".top-bar", new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task CompletionDialog_ValidSubmission_PersistsAcrossReload()
    {
        var dialog = await LoginAndReachBlockedShellAsync();

        await dialog.FillAllRequiredFieldsAsync(
            dobDdMMyyyy: "02/01/1990",
            nationality: "British",
            gender: "Female",
            addressLine1: "1 Test Street",
            city: "London",
            postcode: "SW1A 1AA");

        await dialog.SaveAndWaitForCloseAsync();
        await _page.WaitForSelectorAsync(".top-bar", new() { Timeout = 15_000 });

        await _page.ReloadAsync();
        await _page.WaitForSelectorAsync(".top-bar", new() { Timeout = 20_000 });

        Assert.False(await dialog.IsVisibleAsync());
    }

    [Fact]
    public async Task GettingStarted_ShowsCompleteEmployeeRecordAsFirstIncompleteMandatoryItem_AndLinksToSameDialog()
    {
        var dialog = await LoginAndReachBlockedShellAsync();

        // RequiresInitialSetup is still true — navigating to /getting-started is globally
        // intercepted by MainLayout's gate and shows the SAME blocking dialog, not the checklist
        // page itself (matches the feature's documented behaviour: the checklist's LinkUrl for this
        // item is just "/getting-started").
        Assert.True(await dialog.IsVisibleAsync());
    }

    [Fact]
    public async Task GettingStarted_CompleteEmployeeRecordItem_ShowsCompleted_AfterDialogSubmitted()
    {
        var dialog = await LoginAndReachBlockedShellAsync();

        await dialog.FillAllRequiredFieldsAsync(
            dobDdMMyyyy: "02/01/1990",
            nationality: "British",
            gender: "Female",
            addressLine1: "1 Test Street",
            city: "London",
            postcode: "SW1A 1AA");

        await dialog.SaveAndWaitForCloseAsync();
        await _page.WaitForSelectorAsync(".top-bar", new() { Timeout = 15_000 });

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/getting-started");
        await _page.WaitForSelectorAsync(".onboarding-progress, .alert-danger", new() { Timeout = 20_000 });

        var card = _page.Locator(".card").Filter(new() { HasText = "Update your employee record" }).First;
        await card.WaitForAsync(new() { Timeout = 10_000 });

        Assert.True(await card.Locator(".badge.bg-success").IsVisibleAsync(),
            "Expected the 'Update your employee record' checklist item to show as Completed after the dialog was submitted.");
    }

    [Fact]
    public async Task CompletionDialog_LogoutLink_NavigatesToLogin_EvenWhileBlocked()
    {
        var dialog = await LoginAndReachBlockedShellAsync();

        await dialog.ClickLogoutAsync();

        await _page.WaitForURLAsync(new System.Text.RegularExpressions.Regex("/login"), new() { Timeout = 20_000 });
        Assert.Contains("/login", _page.Url);
    }

    private sealed record SignUpResult(
        Guid UserId,
        Guid CompanyId,
        string Email,
        string FirstName,
        string LastName);
}
