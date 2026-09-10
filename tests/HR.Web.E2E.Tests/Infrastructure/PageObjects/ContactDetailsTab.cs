using System.Net.Http.Json;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the Contact Details tab on the self-service My Profile page.
/// Selectors derived from MyProfileContactDetailsTab.razor (.cd-* CSS classes).
/// </summary>
public sealed class ContactDetailsTab(IPage page)
{
    public async Task WaitForLoadAsync() =>
        await page.WaitForSelectorAsync(".cd-card", new() { Timeout = 15_000 });

    public async Task<bool> IsVisibleAsync() =>
        await page.Locator(".cd-card").IsVisibleAsync();

    // ── Field accessors ────────────────────────────────────────────────────────

    public async Task FillPersonalEmailAsync(string email)
    {
        var input = page.GetByPlaceholder("e.g. name@personal.com");
        await input.ClearAsync();
        await input.FillAsync(email);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillMobilePhoneAsync(string phone)
    {
        var input = page.GetByPlaceholder("e.g. 07700 900000");
        await input.ClearAsync();
        await input.FillAsync(phone);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillHomePhoneAsync(string phone)
    {
        var input = page.GetByPlaceholder("e.g. 01234 567890");
        await input.ClearAsync();
        await input.FillAsync(phone);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillAddressLine1Async(string value)
    {
        var input = page.GetByPlaceholder("Street address");
        await input.ClearAsync();
        await input.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillCityAsync(string value)
    {
        var input = page.GetByPlaceholder("e.g. London");
        await input.ClearAsync();
        await input.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillPostCodeAsync(string value)
    {
        var input = page.GetByPlaceholder("e.g. SW1A 1AA");
        await input.ClearAsync();
        await input.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    // Country is not shown on the contact details form (UK-only for now — the model defaults to
    // "United Kingdom"), so there is no FillCountryAsync.

    // ── Actions ────────────────────────────────────────────────────────────────

    public async Task SaveChangesAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save Changes" }).ClickAsync();
        // Wait for the success banner to appear.
        await page.WaitForSelectorAsync(".cd-success-banner", new() { Timeout = 15_000 });
    }

    /// <summary>Clicks Save without waiting for success — for asserting a validation error instead.</summary>
    public async Task ClickSaveAsync() =>
        await page.GetByRole(AriaRole.Button, new() { Name = "Save Changes" }).ClickAsync();

    // ── Optimistic-concurrency conflict banner (Ticket 2) ─────────────────────
    // MyProfileContactDetailsTab.razor renders the shared <SaveConflictBanner> component: a single
    // `div.alert.alert-warning.save-conflict-banner[role='alert']` containing a "Reload latest
    // values" Syncfusion button, distinct from the generic red `.alert-danger` GlobalError alert.
    // Scope on the component's own `.save-conflict-banner` class (+ role='alert'), not the shared
    // Bootstrap `.alert-warning` alone, and additionally require the "Reload latest values" action
    // so no unrelated warning alert can satisfy strict mode. Match on structure, not text: on a
    // real 409 the razor overrides the component's default Message with its own copy.
    private ILocator ConcurrencyWarningBanner =>
        page.Locator(".save-conflict-banner[role='alert']")
            .Filter(new() { Has = page.GetByRole(AriaRole.Button, new() { Name = "Reload latest values" }) });

    /// <summary>
    /// Clicks Save and waits for the optimistic-concurrency warning banner to appear — i.e. the
    /// save was rejected because the record changed since it was loaded. The caller's entered
    /// values are intentionally left untouched in the form.
    /// </summary>
    public async Task ClickSaveExpectingConflictAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save Changes" }).ClickAsync();
        await ConcurrencyWarningBanner.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
    }

    public Task<bool> IsConcurrencyWarningVisibleAsync() =>
        ConcurrencyWarningBanner.IsVisibleAsync();

    /// <summary>Clicks "Reload latest values" in the concurrency banner and waits for it to clear.</summary>
    public async Task ClickReloadLatestValuesAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Reload latest values" }).ClickAsync();
        await ConcurrencyWarningBanner.WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 20_000 });
    }

    /// <summary>
    /// Bug fix (b): clears the required "Address Line 1" field and blurs it so the EditContext
    /// re-validates and reports a message. EditSectionBase.OnValidationStateChanged then drops any
    /// standing concurrency banner. Waits for the field-level validation message to confirm the
    /// invalid state registered (this tab renders <c>.validation-message</c>, not <c>.is-invalid</c>).
    /// </summary>
    public async Task MakeFormInvalidAsync()
    {
        // A bare FillAsync("") on the Syncfusion-backed HrTextBox doesn't drive its interop, so the
        // EditContext never sees the field change and no validation fires. Clear it "for real" —
        // focus, select-all, Delete — then Tab to commit the blur, the same technique the fill
        // helpers in this codebase use for these inputs.
        var input = page.GetByPlaceholder("Street address");
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        await page.WaitForTimeoutAsync(150);
        await page.Keyboard.PressAsync("Tab");
        await page.WaitForTimeoutAsync(300);
        await page.Locator(".validation-message").First.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task<string?> GetMobilePhoneValueAsync()
    {
        var input = page.GetByPlaceholder("e.g. 07700 900000");
        return await input.IsVisibleAsync() ? (await input.InputValueAsync()).Trim() : null;
    }

    public async Task<bool> IsSuccessBannerVisibleAsync() =>
        await page.Locator(".cd-success-banner").IsVisibleAsync();

    public async Task<bool> HasValidationErrorAsync()
    {
        try
        {
            await page.Locator(".is-invalid").First.WaitForAsync(new() { Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// This tab has no per-field ".is-invalid" styling (see <see cref="HasValidationErrorAsync"/>) —
    /// a failed client-side Validate() instead surfaces as the GlobalError banner
    /// (EditSectionBase sets it to "Please correct the highlighted fields above.").
    /// </summary>
    public async Task<bool> HasGlobalErrorAsync()
    {
        // Checking IsVisibleAsync() immediately after ClickSaveAsync races the save round-trip
        // that renders this banner — give it a short window to appear.
        try
        {
            await page.Locator(".alert-danger").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>Returns the current value of the Work Email field (readonly).</summary>
    public async Task<string?> GetWorkEmailAsync()
    {
        var input = page.Locator(".cd-readonly").First;
        return await input.IsVisibleAsync()
            ? (await input.InputValueAsync()).Trim()
            : null;
    }

    /// <summary>Returns the current value of the Personal Email input via the live DOM property.</summary>
    public async Task<string?> GetPersonalEmailAsync()
    {
        var input = page.GetByPlaceholder("e.g. name@personal.com");
        return await input.IsVisibleAsync()
            ? (await input.InputValueAsync()).Trim()
            : null;
    }

    // ── Accessibility / keyboard accessors (Ticket 7) ─────────────────────────

    /// <summary>The primary "Save Changes" button as a locator.</summary>
    public ILocator SaveButton =>
        page.GetByRole(AriaRole.Button, new() { Name = "Save Changes" });

    /// <summary>Places keyboard focus on the first editable field (Personal Email).</summary>
    public async Task FocusFirstFieldAsync() =>
        await page.GetByPlaceholder("e.g. name@personal.com").ClickAsync();

    /// <summary>Presses Tab once.</summary>
    public Task PressTabAsync() => page.Keyboard.PressAsync("Tab");

    /// <summary>The <c>id</c> of the currently focused element, or null.</summary>
    public Task<string?> FocusedElementIdAsync() =>
        page.EvaluateAsync<string?>("() => document.activeElement && document.activeElement.id ? document.activeElement.id : null");

    /// <summary>True when the focused element is the Save button.</summary>
    public Task<bool> ActiveElementIsSaveButtonAsync() =>
        page.EvaluateAsync<bool>("() => !!document.activeElement && document.activeElement.id === 'cd-save-button'");

    /// <summary>True when the focused element sits inside a <c>role="status"</c> live region.</summary>
    public Task<bool> ActiveElementIsInStatusRegionAsync() =>
        page.EvaluateAsync<bool>("() => !!document.activeElement && !!document.activeElement.closest('[role=\"status\"]')");

    /// <summary>True when the focused element is inside the contact-details form (<c>#cd-form</c>).</summary>
    public Task<bool> ActiveElementIsInFormAsync() =>
        page.EvaluateAsync<bool>("() => { const f = document.getElementById('cd-form'); return !!f && !!document.activeElement && f.contains(document.activeElement); }");

    /// <summary>
    /// True when the focused element belongs to the same <c>.cd-field-group</c> as the control with
    /// the given id — used to assert "focus moved to the field with the validation error".
    /// </summary>
    public Task<bool> ActiveElementIsInFieldGroupOfAsync(string controlId) =>
        page.EvaluateAsync<bool>(
            @"(id) => {
                const el = document.activeElement;
                if (!el) return false;
                const group = el.closest('.cd-field-group');
                return !!group && !!group.querySelector('#' + CSS.escape(id));
            }", controlId);

    /// <summary>
    /// Describes the focused element for the keyboard-order journey: its tag, whether it is a form
    /// control, whether it lies inside the form, whether it is the Save button, and its computed
    /// accessible name (aria-label → aria-labelledby → associated/wrapping &lt;label&gt;).
    /// </summary>
    public async Task<FocusedControlInfo> DescribeActiveElementAsync()
    {
        var raw = await page.EvaluateAsync<string>(
            @"() => {
                const el = document.activeElement;
                if (!el) return '|||';
                const f = document.getElementById('cd-form');
                const inForm = !!f && f.contains(el);
                const tag = (el.tagName || '').toLowerCase();
                const isControl = ['input', 'select', 'textarea', 'button'].includes(tag);
                const isSave = el.id === 'cd-save-button';
                let name = (el.getAttribute('aria-label') || '').trim();
                if (!name) {
                    const lb = el.getAttribute('aria-labelledby');
                    if (lb) {
                        name = lb.split(/\s+/)
                            .map(id => { const n = document.getElementById(id); return n ? n.textContent.trim() : ''; })
                            .join(' ').trim();
                    }
                }
                if (!name && el.id) {
                    const l = document.querySelector('label[for=""' + el.id + '""]');
                    if (l) name = l.textContent.trim();
                }
                if (!name) {
                    const w = el.closest('label');
                    if (w) name = w.textContent.trim();
                }
                if (!name && isSave) name = (el.textContent || '').trim();
                return [inForm, isControl, isSave, tag, name].join('§');
            }");
        var parts = raw.Split('§');
        return new FocusedControlInfo(
            InForm: parts.Length > 0 && parts[0] == "True",
            IsControl: parts.Length > 1 && parts[1] == "True",
            IsSaveButton: parts.Length > 2 && parts[2] == "True",
            Tag: parts.Length > 3 ? parts[3] : "",
            AccessibleName: parts.Length > 4 ? parts[4] : "");
    }

    /// <summary>role attribute of the optimistic-concurrency conflict banner.</summary>
    public Task<string?> ConcurrencyBannerRoleAsync() =>
        page.Locator(".save-conflict-banner").First.GetAttributeAsync("role");

    // ── Ticket 7 follow-up: accessible "saving" announcement ──────────────────

    /// <summary>The always-rendered polite live region that announces the in-flight save.</summary>
    public ILocator SavingStatusRegion => page.Locator("#cd-saving-status");

    /// <summary>Current text content of the saving live region (empty when no save is in flight).</summary>
    public async Task<string> SavingStatusTextAsync() =>
        (await SavingStatusRegion.TextContentAsync() ?? string.Empty).Trim();

    /// <summary>
    /// True when keyboard focus currently sits inside the saving live region specifically
    /// (<c>#cd-saving-status</c>) — used to assert focus is NOT yanked into it during a save. The
    /// success banner is also <c>role="status"</c>, so this keys off the id, not the role.
    /// </summary>
    public Task<bool> ActiveElementIsInSavingRegionAsync() =>
        page.EvaluateAsync<bool>(
            "() => !!document.activeElement && !!document.activeElement.closest('#cd-saving-status')");

    /// <summary>True when the Save button is disabled (duplicate-submit protection while saving).</summary>
    public Task<bool> IsSaveDisabledAsync() => SaveButton.IsDisabledAsync();

    /// <summary>Text of the red <c>role="alert"</c> global error banner, or empty if absent.</summary>
    public async Task<string> ErrorAlertTextAsync()
    {
        var alert = page.Locator(".alert-danger");
        return await alert.CountAsync() > 0
            ? (await alert.First.InnerTextAsync()).Trim()
            : string.Empty;
    }

    private ILocator SuccessDismissButton => page.Locator("button[aria-label='Dismiss confirmation']");
    private ILocator ErrorDismissButton   => page.Locator("button[aria-label='Dismiss error']");

    /// <summary>Focuses the success banner's dismiss button and confirms it holds focus.</summary>
    public async Task FocusSuccessDismissAsync()
    {
        await SuccessDismissButton.FocusAsync();
        await Assertions.Expect(SuccessDismissButton).ToBeFocusedAsync();
    }

    /// <summary>Focuses the error banner's dismiss button and confirms it holds focus.</summary>
    public async Task FocusErrorDismissAsync()
    {
        await ErrorDismissButton.FocusAsync();
        await Assertions.Expect(ErrorDismissButton).ToBeFocusedAsync();
    }

    /// <summary>Activates the currently focused element with the keyboard and waits for the given key name.</summary>
    public Task PressKeyAsync(string key) => page.Keyboard.PressAsync(key);

    /// <summary>Dismisses the success banner via keyboard and waits for it to disappear.</summary>
    public async Task DismissSuccessByKeyboardAsync(string key = "Enter")
    {
        await FocusSuccessDismissAsync();
        await page.Keyboard.PressAsync(key);
        await page.Locator(".cd-success-banner").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    /// <summary>Dismisses the error banner via keyboard and waits for it to disappear.</summary>
    public async Task DismissErrorByKeyboardAsync(string key = "Enter")
    {
        await FocusErrorDismissAsync();
        await page.Keyboard.PressAsync(key);
        await page.Locator(".alert-danger").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    // ── Server-side save control for the contact-details update call ───────────
    // The self-service Contact Details save goes server-side (HR.Web → hrapi), so a Playwright
    // browser-route interceptor cannot hold or fail it. Instead HR.Web exposes E2E_TESTING-only
    // minimal-API endpoints on its own host, keyed by lower-cased employee email, that hold the
    // NEXT server-side PUT .../employees/me/contact-details for that employee.

    /// <summary>
    /// Test-only handle over the HR.Web server-side contact-save control endpoints. Arm it before
    /// clicking Save, wait for the held request to arrive, then either <see cref="ReleaseAsync"/>
    /// (let the real API respond — genuine success) or <see cref="FailAsync"/> (respond HTTP 500).
    /// Always dispose (prefer <c>await using</c>) — disposal best-effort DELETEs the control and
    /// releases any still-held request, so cleanup is guaranteed even on assertion failure and,
    /// being per-email, never affects another test.
    /// </summary>
    public sealed class SaveControl : IAsyncDisposable
    {
        private readonly HttpClient _http;
        private readonly string _base;
        private readonly string _email;

        public SaveControl(string webBaseUrl, string employeeEmail)
        {
            _base = webBaseUrl.TrimEnd('/');
            _email = Uri.EscapeDataString(employeeEmail.ToLowerInvariant());
            _http = new HttpClient();
        }

        private string Url(string suffix = "") => $"{_base}/_e2e/contact-save-control/{_email}{suffix}";

        /// <summary>Arms the control: the NEXT server-side contact-details PUT for this employee is held.</summary>
        public static async Task<SaveControl> ArmAsync(string webBaseUrl, string employeeEmail)
        {
            var ctrl = new SaveControl(webBaseUrl, employeeEmail);
            var response = await ctrl._http.PostAsync(ctrl.Url(), content: null);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Failed to arm the contact-save control for '{employeeEmail}': HTTP {(int)response.StatusCode}.");
            return ctrl;
        }

        private sealed record Status(bool arrived, int requestCount, bool resolved);

        private async Task<Status?> GetStatusAsync()
        {
            var response = await _http.GetAsync(Url());
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<Status>();
        }

        /// <summary>Polls until the held server-side PUT has arrived. Default timeout 20s.</summary>
        public async Task WaitUntilRequestArrivedAsync(TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
            while (DateTime.UtcNow < deadline)
            {
                var status = await GetStatusAsync();
                if (status is { arrived: true }) return;
                await Task.Delay(200);
            }

            throw new TimeoutException(
                $"No server-side contact-details PUT arrived for employee '{Uri.UnescapeDataString(_email)}' " +
                $"within {(timeout ?? TimeSpan.FromSeconds(20)).TotalSeconds:0}s.");
        }

        /// <summary>Number of server-side save (PUT) requests seen by the control (0 if not armed).</summary>
        public async Task<int> RequestCountAsync()
        {
            var status = await GetStatusAsync();
            return status?.requestCount ?? 0;
        }

        /// <summary>Lets the held request through to the real API (a genuine success). Does not wait for completion.</summary>
        public async Task ReleaseAsync()
        {
            var response = await _http.PostAsync(Url("/release"), content: null);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Failed to release the held contact save: HTTP {(int)response.StatusCode}.");
        }

        /// <summary>Responds HTTP 500 to the held request. Does not wait for the UI to react.</summary>
        public async Task FailAsync()
        {
            var response = await _http.PostAsync(Url("/fail"), content: null);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Failed to fail the held contact save: HTTP {(int)response.StatusCode}.");
        }

        public async ValueTask DisposeAsync()
        {
            try { await _http.DeleteAsync(Url()); } catch { /* best-effort cleanup */ }
            _http.Dispose();
        }
    }

    public sealed record FocusedControlInfo(
        bool InForm, bool IsControl, bool IsSaveButton, string Tag, string AccessibleName);
}
