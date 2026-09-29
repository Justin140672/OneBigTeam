using System.Net.Http.Json;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class ContactDetailsTab(IPage page)
{
    public async Task WaitForLoadAsync() =>
        await page.WaitForSelectorAsync(".cd-card", new() { Timeout = 15_000 });

    public async Task<bool> IsVisibleAsync() =>
        await page.Locator(".cd-card").IsVisibleAsync();


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



    public async Task SaveChangesAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save Changes" }).ClickAsync();
        await page.WaitForSelectorAsync(".cd-success-banner", new() { Timeout = 15_000 });
    }

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

    public async Task ClickReloadLatestValuesAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Reload latest values" }).ClickAsync();
        await ConcurrencyWarningBanner.WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 20_000 });
    }

    public async Task MakeFormInvalidAsync()
    {
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

    public async Task<bool> HasGlobalErrorAsync()
    {
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

    public async Task<string?> GetWorkEmailAsync()
    {
        var input = page.Locator(".cd-readonly").First;
        return await input.IsVisibleAsync()
            ? (await input.InputValueAsync()).Trim()
            : null;
    }

    public async Task<string?> GetPersonalEmailAsync()
    {
        var input = page.GetByPlaceholder("e.g. name@personal.com");
        return await input.IsVisibleAsync()
            ? (await input.InputValueAsync()).Trim()
            : null;
    }

    // ── Accessibility / keyboard accessors (Ticket 7) ─────────────────────────

    public ILocator SaveButton => page.Locator("#cd-save-button");

    public async Task FocusFirstFieldAsync() =>
        await page.GetByPlaceholder("e.g. name@personal.com").ClickAsync();

    public Task PressTabAsync() => page.Keyboard.PressAsync("Tab");

    public Task<string?> FocusedElementIdAsync() =>
        page.EvaluateAsync<string?>("() => document.activeElement && document.activeElement.id ? document.activeElement.id : null");

    public Task<bool> ActiveElementIsSaveButtonAsync() =>
        page.EvaluateAsync<bool>("() => !!document.activeElement && document.activeElement.id === 'cd-save-button'");

    public Task<bool> ActiveElementIsInStatusRegionAsync() =>
        page.EvaluateAsync<bool>("() => !!document.activeElement && !!document.activeElement.closest('[role=\"status\"]')");

    public Task<bool> ActiveElementIsInFormAsync() =>
        page.EvaluateAsync<bool>("() => { const f = document.getElementById('cd-form'); return !!f && !!document.activeElement && f.contains(document.activeElement); }");

    public Task<bool> ActiveElementIsInFieldGroupOfAsync(string controlId) =>
        page.EvaluateAsync<bool>(
            @"(id) => {
                const el = document.activeElement;
                if (!el) return false;
                const group = el.closest('.cd-field-group');
                return !!group && !!group.querySelector('#' + CSS.escape(id));
            }", controlId);

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
                return [inForm ? 'True' : 'False', isControl ? 'True' : 'False', isSave ? 'True' : 'False', tag, name].join('§');
            }");
        var parts = raw.Split('§');
        return new FocusedControlInfo(
            InForm: parts.Length > 0 && parts[0] == "True",
            IsControl: parts.Length > 1 && parts[1] == "True",
            IsSaveButton: parts.Length > 2 && parts[2] == "True",
            Tag: parts.Length > 3 ? parts[3] : "",
            AccessibleName: parts.Length > 4 ? parts[4] : "");
    }

    public Task<string?> ConcurrencyBannerRoleAsync() =>
        page.Locator(".save-conflict-banner").First.GetAttributeAsync("role");

    // ── Ticket 7 follow-up: accessible "saving" announcement ──────────────────

    public ILocator SavingStatusRegion => page.Locator("#cd-saving-status");

    public async Task<string> SavingStatusTextAsync() =>
        (await SavingStatusRegion.TextContentAsync() ?? string.Empty).Trim();

    public async Task WaitForSavingStateRenderedAsync()
    {
        await Assertions.Expect(SavingStatusRegion).ToContainTextAsync("Saving contact details", new() { Timeout = 15_000 });
        await Assertions.Expect(SaveButton).ToBeDisabledAsync(new() { Timeout = 15_000 });
    }

    public Task<bool> ActiveElementIsInSavingRegionAsync() =>
        page.EvaluateAsync<bool>(
            "() => !!document.activeElement && !!document.activeElement.closest('#cd-saving-status')");

    public Task<bool> IsSaveDisabledAsync() => SaveButton.IsDisabledAsync();

    public async Task<string> ErrorAlertTextAsync()
    {
        var alert = page.Locator(".alert-danger");
        return await alert.CountAsync() > 0
            ? (await alert.First.InnerTextAsync()).Trim()
            : string.Empty;
    }

    private ILocator SuccessDismissButton => page.Locator("button[aria-label='Dismiss confirmation']");
    private ILocator ErrorDismissButton   => page.Locator("button[aria-label='Dismiss error']");

    public async Task FocusSuccessDismissAsync()
    {
        await SuccessDismissButton.FocusAsync();
        await Assertions.Expect(SuccessDismissButton).ToBeFocusedAsync();
    }

    public async Task FocusErrorDismissAsync()
    {
        await ErrorDismissButton.FocusAsync();
        await Assertions.Expect(ErrorDismissButton).ToBeFocusedAsync();
    }

    public Task PressKeyAsync(string key) => page.Keyboard.PressAsync(key);

    public async Task DismissSuccessByKeyboardAsync(string key = "Enter")
    {
        await FocusSuccessDismissAsync();
        await page.Keyboard.PressAsync(key);
        await page.Locator(".cd-success-banner").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    public async Task DismissErrorByKeyboardAsync(string key = "Enter")
    {
        await FocusErrorDismissAsync();
        await page.Keyboard.PressAsync(key);
        await page.Locator(".alert-danger").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }


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

        public async Task<int> RequestCountAsync()
        {
            var status = await GetStatusAsync();
            return status?.requestCount ?? 0;
        }

        public async Task ReleaseAsync()
        {
            var response = await _http.PostAsync(Url("/release"), content: null);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Failed to release the held contact save: HTTP {(int)response.StatusCode}.");
        }

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
