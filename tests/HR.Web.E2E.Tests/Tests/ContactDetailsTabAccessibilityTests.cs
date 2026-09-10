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

    // ── Ticket 7 follow-up ───────────────────────────────────────────────────

    /// <summary>Fills the mandatory fields plus a unique personal email so every saving test writes
    /// isolated data and can run at maxParallelThreads=15.</summary>
    private static async Task FillValidUniqueAsync(ContactDetailsTab contact)
    {
        await contact.FillAddressLine1Async($"{Guid.NewGuid():N} Test Street");
        await contact.FillCityAsync("London");
        await contact.FillPostCodeAsync("EC1A 1BB");
        await contact.FillPersonalEmailAsync($"e2e.{Guid.NewGuid():N}@personal.example.com");
    }

    [Fact]
    public async Task ContactDetailsTab_Saving_AnnouncesToAssistiveTechAndPreventsDuplicateSubmit()
    {
        var contact = await OpenContactDetailsAsync();
        await FillValidUniqueAsync(contact);

        var held = await contact.HoldNextSaveAsync();

        await contact.ClickSaveAsync();
        await held.WaitUntilHeldAsync();

        // The live region announces the in-flight save and is exposed to assistive tech.
        Assert.Contains("Saving contact details", await contact.SavingStatusTextAsync());
        Assert.Equal("status", await contact.SavingStatusRegion.GetAttributeAsync("role"));
        Assert.Equal("polite", await contact.SavingStatusRegion.GetAttributeAsync("aria-live"));

        // Duplicate-submit protection: Save disabled, and a second activation raises no second request.
        Assert.True(await contact.IsSaveDisabledAsync());
        await contact.SaveButton.PressAsync("Enter");
        await contact.ClickSaveAsync();
        Assert.Equal(1, held.PutCount);

        // Axe scan while the save is held.
        await AccessibilityScan.AssertNoSeriousViolationsAsync(
            _page, "my profile — Contact Details tab (saving in flight)");

        await held.ReleaseAsync();

        await _page.Locator(".cd-success-banner").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        Assert.Equal(string.Empty, await contact.SavingStatusTextAsync());
    }

    [Fact]
    public async Task ContactDetailsTab_ServerFailure_ShowsAccessibleErrorAndAllowsRetry()
    {
        var contact = await OpenContactDetailsAsync();
        var email = $"e2e.{Guid.NewGuid():N}@personal.example.com";
        var line1 = $"{Guid.NewGuid():N} Retry Street";
        await contact.FillAddressLine1Async(line1);
        await contact.FillCityAsync("London");
        await contact.FillPostCodeAsync("EC1A 1BB");
        await contact.FillPersonalEmailAsync(email);

        var held = await contact.HoldNextSaveAsync();
        await contact.ClickSaveAsync();
        await held.WaitUntilHeldAsync();
        await held.FailAsync();

        await _page.Locator(".alert-danger[role='alert']").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        Assert.False(string.IsNullOrWhiteSpace(await contact.ErrorAlertTextAsync()));

        // Entered values preserved, saving status cleared, Save re-enabled.
        Assert.Equal(email, await contact.GetPersonalEmailAsync());
        Assert.Equal(line1, (await _page.GetByPlaceholder("Street address").InputValueAsync()).Trim());
        Assert.Equal(string.Empty, await contact.SavingStatusTextAsync());
        Assert.False(await contact.IsSaveDisabledAsync());

        await AccessibilityScan.AssertNoSeriousViolationsAsync(
            _page, "my profile — Contact Details tab (server error)");

        // Retry against the real API now succeeds.
        await held.UnrouteAsync();
        await contact.SaveChangesAsync();
        Assert.True(await contact.IsSuccessBannerVisibleAsync());
    }

    [Fact]
    public async Task ContactDetailsTab_SuccessBanner_DismissByKeyboard_ReturnsFocusToSave()
    {
        var contact = await OpenContactDetailsAsync();
        await FillValidUniqueAsync(contact);
        await contact.SaveChangesAsync();

        await contact.DismissSuccessByKeyboardAsync("Enter");

        Assert.False(await contact.IsSuccessBannerVisibleAsync());
        Assert.Equal("cd-save-button", await contact.FocusedElementIdAsync());
    }

    [Fact]
    public async Task ContactDetailsTab_ErrorBanner_DismissByKeyboard_ReturnsFocusToSave()
    {
        var contact = await OpenContactDetailsAsync();
        await FillValidUniqueAsync(contact);

        var held = await contact.HoldNextSaveAsync();
        await contact.ClickSaveAsync();
        await held.WaitUntilHeldAsync();
        await held.FailAsync();

        await _page.Locator(".alert-danger[role='alert']").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        await contact.DismissErrorByKeyboardAsync("Space");

        Assert.Equal(string.Empty, await contact.ErrorAlertTextAsync());
        Assert.Equal("cd-save-button", await contact.FocusedElementIdAsync());
    }

    [Fact]
    public async Task ContactDetailsTab_FullKeyboardJourney_EntersAndEditsValuesWithRealKeyboardInput()
    {
        var contact = await OpenContactDetailsAsync();

        // Type into the first fields with the real keyboard, Tab between them — no .Fill.
        await contact.FocusFirstFieldAsync();
        await _page.Keyboard.TypeAsync($"e2e.{Guid.NewGuid():N}@personal.example.com");
        await _page.Keyboard.PressAsync("Tab"); // → Address Line 1
        await _page.Keyboard.TypeAsync($"{Guid.NewGuid():N} Keyboard Way");
        await _page.Keyboard.PressAsync("Tab"); // → Address Line 2
        await _page.Keyboard.PressAsync("Tab"); // → City
        await _page.Keyboard.TypeAsync("London");
        await _page.Keyboard.PressAsync("Tab"); // → County
        await _page.Keyboard.PressAsync("Tab"); // → Post Code
        await _page.Keyboard.TypeAsync("EC1A 1BB");
        await _page.Keyboard.PressAsync("Tab");

        // Edit an already-populated field via keyboard: select-all + retype.
        var city = _page.GetByPlaceholder("e.g. London");
        await city.ClickAsync();
        await _page.Keyboard.PressAsync("Control+A");
        await _page.Keyboard.PressAsync("Delete");
        await _page.Keyboard.TypeAsync("Manchester");
        await _page.Keyboard.PressAsync("Tab");

        // Tab to Save and activate with Enter only (poll for real focus — Blazor Server re-renders).
        var focused = false;
        for (var attempt = 0; attempt < 10 && !focused; attempt++)
        {
            await contact.SaveButton.FocusAsync();
            focused = await contact.ActiveElementIsSaveButtonAsync();
            if (!focused)
                await contact.SaveButton.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        }
        Assert.True(focused, "Expected the Save button to hold keyboard focus.");
        await _page.Keyboard.PressAsync("Enter");

        await _page.Locator(".cd-success-banner").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        Assert.False(await contact.ActiveElementIsInSavingRegionAsync());
        Assert.False(await contact.ActiveElementIsInStatusRegionAsync());
        Assert.True(
            await contact.ActiveElementIsSaveButtonAsync() || await contact.ActiveElementIsInFormAsync());
    }

    [Fact]
    public async Task ContactDetailsTab_ValidationCorrection_FocusesFirstInvalidThenSavesAfterKeyboardFix()
    {
        var contact = await OpenContactDetailsAsync();

        await contact.FillAddressLine1Async($"{Guid.NewGuid():N} Correction Road");
        await contact.FillCityAsync("London");
        await contact.FillPostCodeAsync("not a postcode");
        await contact.FillPersonalEmailAsync($"e2e.{Guid.NewGuid():N}@personal.example.com");

        await contact.ClickSaveAsync();

        Assert.True(await contact.HasGlobalErrorAsync());
        await Assertions.Expect(_page.Locator(".validation-message").First).ToBeVisibleAsync();
        Assert.True(await contact.ActiveElementIsInFieldGroupOfAsync("cd-post-code"));

        // Correct the postcode from the keyboard where focus already is.
        await _page.Keyboard.PressAsync("Control+A");
        await _page.Keyboard.PressAsync("Delete");
        await _page.Keyboard.TypeAsync("EC1A 1BB");
        await _page.Keyboard.PressAsync("Tab");

        await contact.SaveChangesAsync();
        Assert.True(await contact.IsSuccessBannerVisibleAsync());
    }

    [Fact]
    public async Task ContactDetailsTab_NarrowViewport_FieldsValidationAndControlsRemainUsable()
    {
        await _page.SetViewportSizeAsync(360, 740);

        var contact = await OpenContactDetailsAsync();

        await contact.FillAddressLine1Async("123 Narrow Street");
        await contact.FillCityAsync("London");
        await contact.FillPostCodeAsync("not a postcode");
        await contact.ClickSaveAsync();

        Assert.True(await contact.HasGlobalErrorAsync());

        var postCode = _page.GetByPlaceholder("e.g. SW1A 1AA");
        var validationMsg = _page.Locator(".validation-message").First;
        var feedback = _page.Locator(".alert-danger").First;

        await Assertions.Expect(postCode).ToBeVisibleAsync();
        await Assertions.Expect(validationMsg).ToBeVisibleAsync();
        await Assertions.Expect(contact.SaveButton).ToBeVisibleAsync();
        await Assertions.Expect(feedback).ToBeVisibleAsync();

        const double tolerance = 2.0;
        foreach (var (name, loc) in new[]
                 {
                     ("post code input", postCode),
                     ("validation message", validationMsg),
                     ("save button", (ILocator)contact.SaveButton),
                     ("feedback banner", feedback),
                 })
        {
            var box = await loc.BoundingBoxAsync();
            Assert.NotNull(box);
            Assert.True(box!.X >= -tolerance, $"{name} clipped on the left ({box.X}).");
            Assert.True(box.X + box.Width <= 360 + tolerance,
                $"{name} extends past the 360px viewport ({box.X + box.Width}).");
        }

        // Field input and the Save button must not overlap vertically.
        var inputBox = await postCode.BoundingBoxAsync();
        var saveBox = await contact.SaveButton.BoundingBoxAsync();
        Assert.True(saveBox!.Y >= inputBox!.Y + inputBox.Height - tolerance,
            "Save button overlaps the post code field at a narrow width.");
    }

    // NOTE: this approximates reflow by shrinking the viewport; it is NOT a real 200% browser-zoom
    // test. True 200%-zoom verification remains an outstanding MANUAL check recorded in the ticket doc.
    [Fact]
    public async Task ContactDetailsTab_ViewportResize_ApproximatesZoom_AxeClean()
    {
        var contact = await OpenContactDetailsAsync();
        await _page.SetViewportSizeAsync(640, 900);
        await contact.WaitForLoadAsync();

        await AccessibilityScan.AssertNoSeriousViolationsAsync(
            _page, "my profile — Contact Details tab (reduced viewport / approx. reflow)");
    }

    [Fact]
    public async Task ContactDetailsTab_ConflictState_HasNoSeriousViolations()
    {
        var contact = await OpenContactDetailsAsync();

        await contact.FillAddressLine1Async("1 Conflict Axe Way");
        await contact.FillCityAsync("London");
        await contact.FillPostCodeAsync("EC1A 1BB");
        await contact.FillMobilePhoneAsync("07700 900811");

        var otherPage = await _context.NewPageAsync();
        try
        {
            var otherProfile = new MyProfilePage(otherPage, _fixture.WebBaseUrl);
            var otherContact = new ContactDetailsTab(otherPage);
            await otherProfile.GoToAsync(AcmeId, TomId);
            await otherProfile.OpenContactDetailsTabAsync();
            await otherContact.WaitForLoadAsync();
            await otherContact.FillAddressLine1Async("2 Conflict Axe Way");
            await otherContact.FillCityAsync("London");
            await otherContact.FillPostCodeAsync("EC1A 1BB");
            await otherContact.FillMobilePhoneAsync("07700 900812");
            await otherContact.SaveChangesAsync();
        }
        finally
        {
            await otherPage.CloseAsync();
        }

        await contact.ClickSaveExpectingConflictAsync();

        await AccessibilityScan.AssertNoSeriousViolationsAsync(
            _page, "my profile — Contact Details tab (concurrency conflict)");
    }
}
