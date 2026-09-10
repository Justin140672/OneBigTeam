using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Ticket 7 ("Make employee contact details accessible in every form state"): accessibility gate
/// for the self-service My Profile &gt; Contact Details tab (MyProfileContactDetailsTab.razor) as
/// Tom Williams. Covers the axe-core WCAG 2.0 A/AA scan in the initial, validation-error and
/// save-success states plus keyboard-only focus behaviour: logical Tab order with an accessible
/// name on every control, no unnecessary focus movement to the success live-region, predictable
/// focus to the first invalid field after a failed submit, and focus landing on the first form
/// field after recovering from an optimistic-concurrency conflict.
///
/// Each [Fact] gets its own login + page via the base, uses only concrete locator/state waits, and
/// writes unique data where it saves — so it is deterministic at maxParallelThreads=15.
/// </summary>
public sealed class ContactDetailsTabAccessibilityTests(EmployeePersonaFixture fixture)
    : RoleE2ETestBase<EmployeePersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private const string TomEmail = "tom.williams@acme.example";

    private async Task<ContactDetailsTab> OpenContactDetailsAsync()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var contact = new ContactDetailsTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenContactDetailsTabAsync();
        await contact.WaitForLoadAsync();
        return contact;
    }

    // ── a. Initial state ─────────────────────────────────────────────────────

    [Fact]
    public async Task ContactDetailsTab_InitialState_HasNoSeriousViolations()
    {
        await OpenContactDetailsAsync();

        await AccessibilityScan.AssertNoSeriousViolationsAsync(
            _page, "my profile — Contact Details tab (initial)");
    }

    // ── b. Validation-error state ────────────────────────────────────────────

    [Fact]
    public async Task ContactDetailsTab_ValidationErrorState_HasNoSeriousViolations()
    {
        var contact = await OpenContactDetailsAsync();

        await contact.FillAddressLine1Async("123 Test Street");
        await contact.FillCityAsync("London");
        await contact.FillPostCodeAsync("not a postcode");

        await contact.ClickSaveAsync();

        Assert.True(await contact.HasGlobalErrorAsync(),
            "Expected the validation-error banner before scanning the error state");

        await AccessibilityScan.AssertNoSeriousViolationsAsync(
            _page, "my profile — Contact Details tab (validation error)");
    }

    // ── c. Save-success state ────────────────────────────────────────────────

    [Fact]
    public async Task ContactDetailsTab_SaveSuccessState_HasNoSeriousViolations()
    {
        var contact = await OpenContactDetailsAsync();

        await contact.FillAddressLine1Async("123 Success Street");
        await contact.FillCityAsync("London");
        await contact.FillPostCodeAsync("EC1A 1BB");
        await contact.FillPersonalEmailAsync($"e2e.{Guid.NewGuid():N}@personal.example.com");

        await contact.SaveChangesAsync();

        Assert.True(await contact.IsSuccessBannerVisibleAsync(),
            "Expected the success banner before scanning the success state");

        await AccessibilityScan.AssertNoSeriousViolationsAsync(
            _page, "my profile — Contact Details tab (save success)");
    }

    // ── d. Keyboard-only journey: logical order, named controls, no focus yank ─

    [Fact]
    public async Task ContactDetailsTab_KeyboardJourney_HasLogicalOrderAndDoesNotYankFocusToBanner()
    {
        var contact = await OpenContactDetailsAsync();

        await contact.FillAddressLine1Async("1 Keyboard Way");
        await contact.FillCityAsync("London");
        await contact.FillPostCodeAsync("EC1A 1BB");
        await contact.FillPersonalEmailAsync($"e2e.{Guid.NewGuid():N}@personal.example.com");

        // Start from the first editable field and Tab forward through the form. Every focusable
        // control we land on inside the form must expose an accessible name, and we must reach the
        // Save button within a sane number of tab stops (proving the order is not broken/looping).
        await contact.FocusFirstFieldAsync();

        var reachedSave = false;
        var visitedControls = 0;
        for (var i = 0; i < 25 && !reachedSave; i++)
        {
            var info = await contact.DescribeActiveElementAsync();

            if (info.IsSaveButton)
            {
                reachedSave = true;
                break;
            }

            if (info.InForm && info.IsControl)
            {
                visitedControls++;
                Assert.False(string.IsNullOrWhiteSpace(info.AccessibleName),
                    $"Focusable {info.Tag} control in the contact details form has no accessible name (tab stop {i}).");
            }

            await contact.PressTabAsync();
        }

        Assert.True(reachedSave, "Tabbing forward from the first field never reached the Save button.");
        Assert.True(visitedControls >= 3,
            "Expected to Tab through several named form controls before the Save button.");

        // Activate Save with the keyboard (poll for real focus first — Blazor Server can re-render
        // the button node between FocusAsync and the keypress).
        var focused = false;
        for (var attempt = 0; attempt < 10 && !focused; attempt++)
        {
            await contact.SaveButton.FocusAsync();
            focused = await contact.ActiveElementIsSaveButtonAsync();
            if (!focused)
                await contact.SaveButton.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        }
        Assert.True(focused, "Expected the Save button to hold keyboard focus before activating it.");

        await _page.Keyboard.PressAsync("Enter");

        await _page.Locator(".cd-success-banner").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        // Focus must NOT be yanked into the status/live-region banner — it should stay on the Save
        // button (or at least remain within the form), not jump to the announcement.
        Assert.False(await contact.ActiveElementIsInStatusRegionAsync(),
            "Focus was moved into the success live-region — unnecessary focus movement.");
        Assert.True(
            await contact.ActiveElementIsSaveButtonAsync() || await contact.ActiveElementIsInFormAsync(),
            "After saving by keyboard, focus should remain on the Save button or within the form.");
    }

    // ── e. Validation-failure moves focus to the first invalid field ─────────

    [Fact]
    public async Task ContactDetailsTab_ValidationFailure_MovesFocusToFirstInvalidField()
    {
        var contact = await OpenContactDetailsAsync();

        await contact.FillAddressLine1Async("123 Test Street");
        await contact.FillCityAsync("London");
        await contact.FillPostCodeAsync("not a postcode");

        await contact.ClickSaveAsync();

        Assert.True(await contact.HasGlobalErrorAsync(),
            "Expected a validation error for an invalid postcode");

        await Assertions.Expect(_page.Locator(".validation-message").First).ToBeVisibleAsync();

        Assert.True(await contact.ActiveElementIsInFieldGroupOfAsync("cd-post-code"),
            "After a failed submit, focus should move to the first invalid field (Post Code).");
    }

    // ── f. Concurrency conflict banner is an alert; reload restores field focus ─

    [Fact]
    public async Task ContactDetailsTab_ConcurrencyConflict_BannerIsAlert_AndReloadFocusesFirstField()
    {
        var contact = await OpenContactDetailsAsync();

        await contact.FillAddressLine1Async("1 Conflict Way");
        await contact.FillCityAsync("London");
        await contact.FillPostCodeAsync("EC1A 1BB");
        await contact.FillMobilePhoneAsync("07700 900801");

        // Second tab in the same authenticated context saves first, bumping the record version.
        var otherPage = await _context.NewPageAsync();
        try
        {
            var otherProfile = new MyProfilePage(otherPage, _fixture.WebBaseUrl);
            var otherContact = new ContactDetailsTab(otherPage);
            await otherProfile.GoToAsync(AcmeId, TomId);
            await otherProfile.OpenContactDetailsTabAsync();
            await otherContact.WaitForLoadAsync();
            await otherContact.FillAddressLine1Async("2 Conflict Way");
            await otherContact.FillCityAsync("London");
            await otherContact.FillPostCodeAsync("EC1A 1BB");
            await otherContact.FillMobilePhoneAsync("07700 900802");
            await otherContact.SaveChangesAsync();
        }
        finally
        {
            await otherPage.CloseAsync();
        }

        await contact.ClickSaveExpectingConflictAsync();

        Assert.Equal("alert", await contact.ConcurrencyBannerRoleAsync());

        await contact.ClickReloadLatestValuesAsync();

        // The tab moves focus to the first form field (Personal Email) after adopting fresh values.
        await Assertions.Expect(_page.GetByPlaceholder("e.g. name@personal.com")).ToBeFocusedAsync();
        Assert.Equal("cd-personal-email", await contact.FocusedElementIdAsync());
    }
}
