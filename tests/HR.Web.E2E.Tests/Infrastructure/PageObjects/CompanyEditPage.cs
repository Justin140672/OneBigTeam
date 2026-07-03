using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the company edit page (/companies/{id}/edit).
/// Covers the Profile and Settings tabs.
/// </summary>
public sealed class CompanyEditPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/edit");
        await page.WaitForSelectorAsync("[role='tablist']", new() { Timeout = 20_000 });
    }

    // ── Tab navigation ─────────────────────────────────────────────────────────

    public async Task OpenProfileTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Profile" }).ClickAsync();
        await page.WaitForSelectorAsync(".card", new() { Timeout = 15_000 });
    }

    public async Task OpenSettingsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Settings" }).ClickAsync();
        await page.WaitForSelectorAsync(".card", new() { Timeout = 15_000 });
    }

    public async Task OpenAddressesTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Addresses" }).ClickAsync();
        await page.WaitForSelectorAsync(".card", new() { Timeout = 15_000 });
    }

    // ── Profile tab ────────────────────────────────────────────────────────────

    /// <summary>Returns the company name shown in the h1 heading.</summary>
    public async Task<string> GetCompanyNameAsync() =>
        (await page.Locator("h1").TextContentAsync())?.Trim() ?? "";

    public async Task<bool> IsActiveAsync() =>
        await page.Locator("h1 ~ .badge.bg-success, .badge.bg-success").First.IsVisibleAsync();

    // ── Settings tab ───────────────────────────────────────────────────────────

    /// <summary>Returns true if a working-week day checkbox (e.g. "Monday") is currently checked.</summary>
    public async Task<bool> IsWorkingDayCheckedAsync(string dayName)
    {
        var label = page.Locator("label").Filter(new() { HasText = dayName }).First;
        // The associated checkbox precedes the label in the working-week group.
        var checkbox = label.Locator("input[type='checkbox']");
        return await checkbox.IsCheckedAsync();
    }

    /// <summary>
    /// Sets a working-week day checkbox (e.g. "Monday") to the desired checked state.
    /// Only clicks if the current state differs from the requested one.
    /// </summary>
    public async Task SetWorkingDayAsync(string dayName, bool isChecked)
    {
        var current = await IsWorkingDayCheckedAsync(dayName);
        if (current != isChecked)
        {
            var label = page.Locator("label").Filter(new() { HasText = dayName }).First;
            await label.ClickAsync();
        }
    }

    /// <summary>Returns the current value of the Time Zone text field.</summary>
    public async Task<string> GetTimeZoneAsync() =>
        await page.Locator("input.e-textbox[placeholder='Time Zone']").InputValueAsync();

    /// <summary>Sets the Time Zone text field.</summary>
    public async Task SetTimeZoneAsync(string value)
    {
        var input = page.Locator("input.e-textbox[placeholder='Time Zone']");
        await input.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    /// <summary>Returns the current value of the Locale text field.</summary>
    public async Task<string> GetLocaleAsync() =>
        await page.Locator("input.e-textbox[placeholder='Locale']").InputValueAsync();

    /// <summary>Sets the Locale text field.</summary>
    public async Task SetLocaleAsync(string value)
    {
        var input = page.Locator("input.e-textbox[placeholder='Locale']");
        await input.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    /// <summary>Sets the "Hours Per Day" numeric field.</summary>
    public async Task SetHoursPerDayAsync(decimal hours)
    {
        var input = page.Locator("input.e-numerictextbox[placeholder='Hours Per Day']");
        await input.FillAsync(hours.ToString("0.#"));
        await page.Keyboard.PressAsync("Tab");
    }

    /// <summary>Returns the current value of the "Hours Per Day" numeric field.</summary>
    public async Task<decimal> GetHoursPerDayAsync()
    {
        var input = page.Locator("input.e-numerictextbox[placeholder='Hours Per Day']");
        var value = await input.InputValueAsync();
        return decimal.Parse(value);
    }

    /// <summary>Sets the "Default Holiday Allowance (days)" numeric field.</summary>
    public async Task SetDefaultHolidayAllowanceAsync(decimal days)
    {
        var input = page.Locator("input.e-numerictextbox[placeholder='Default Holiday Allowance (days)']");
        await input.FillAsync(days.ToString("0.#"));
        await page.Keyboard.PressAsync("Tab");
    }

    /// <summary>Returns the current value of the "Default Holiday Allowance (days)" numeric field.</summary>
    public async Task<decimal> GetDefaultHolidayAllowanceAsync()
    {
        var input = page.Locator("input.e-numerictextbox[placeholder='Default Holiday Allowance (days)']");
        var value = await input.InputValueAsync();
        return decimal.Parse(value);
    }

    /// <summary>Sets the "Probation Months" numeric field.</summary>
    public async Task SetProbationMonthsAsync(int months)
    {
        var input = page.Locator("input.e-numerictextbox[placeholder='Probation Months']");
        await input.FillAsync(months.ToString());
        await page.Keyboard.PressAsync("Tab");
    }

    /// <summary>Returns the current value of the "Probation Months" numeric field.</summary>
    public async Task<int> GetProbationMonthsAsync()
    {
        var input = page.Locator("input.e-numerictextbox[placeholder='Probation Months']");
        var value = await input.InputValueAsync();
        return int.Parse(value);
    }

    /// <summary>
    /// Selects a value from the "Leave Year Start Month" dropdown.
    /// </summary>
    public async Task SelectLeaveYearStartMonthAsync(string monthName)
    {
        var group = page.Locator(".col-md-3")
            .Filter(new() { HasText = "Leave Year Start Month" })
            .First;
        await group.Locator("span[role='combobox']").First.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        await page.Locator(".e-popup.e-ddl .e-list-item")
            .Filter(new() { HasText = monthName })
            .First
            .ClickAsync();
    }

    /// <summary>Returns the currently displayed value of the "Leave Year Start Month" dropdown.</summary>
    public async Task<string> GetLeaveYearStartMonthAsync()
    {
        var group = page.Locator(".col-md-3")
            .Filter(new() { HasText = "Leave Year Start Month" })
            .First;
        var combobox = group.Locator("span[role='combobox']").First;
        return (await combobox.Locator("input").InputValueAsync()).Trim();
    }

    /// <summary>Returns true if the "Exclude public holidays from sickness" checkbox is currently checked.</summary>
    public async Task<bool> IsExcludePublicHolidaysFromSicknessCheckedAsync()
    {
        var wrapper = page.Locator(".e-checkbox-wrapper")
            .Filter(new() { HasText = "Exclude public holidays from sickness" });
        return await wrapper.Locator("input[type='checkbox']").IsCheckedAsync();
    }

    /// <summary>
    /// Sets the "Exclude public holidays from sickness" checkbox to the desired checked state.
    /// Only clicks if the current state differs from the requested one.
    /// </summary>
    public async Task SetExcludePublicHolidaysFromSicknessAsync(bool isChecked)
    {
        var current = await IsExcludePublicHolidaysFromSicknessCheckedAsync();
        if (current != isChecked)
        {
            var wrapper = page.Locator(".e-checkbox-wrapper")
                .Filter(new() { HasText = "Exclude public holidays from sickness" });
            await wrapper.Locator("label").ClickAsync();
        }
    }

    /// <summary>Returns true if the "Exclude public holidays from leave" checkbox is currently checked.</summary>
    public async Task<bool> IsExcludePublicHolidaysFromLeaveCheckedAsync()
    {
        var wrapper = page.Locator(".e-checkbox-wrapper")
            .Filter(new() { HasText = "Exclude public holidays from leave" });
        return await wrapper.Locator("input[type='checkbox']").IsCheckedAsync();
    }

    /// <summary>
    /// Sets the "Exclude public holidays from leave" checkbox to the desired checked state.
    /// Only clicks if the current state differs from the requested one.
    /// </summary>
    public async Task SetExcludePublicHolidaysFromLeaveAsync(bool isChecked)
    {
        var current = await IsExcludePublicHolidaysFromLeaveCheckedAsync();
        if (current != isChecked)
        {
            var wrapper = page.Locator(".e-checkbox-wrapper")
                .Filter(new() { HasText = "Exclude public holidays from leave" });
            await wrapper.Locator("label").ClickAsync();
        }
    }

    /// <summary>Sets the "Fit Note Required After (Days)" numeric field. Pass null to clear it.</summary>
    public async Task SetFitNoteRequiredAfterDaysAsync(int? days)
    {
        var input = page.Locator("input.e-numerictextbox[placeholder='Fit Note Required After (Days)']");
        await input.FillAsync(days?.ToString() ?? "");
        await page.Keyboard.PressAsync("Tab");
    }

    /// <summary>Returns the current value of the "Fit Note Required After (Days)" numeric field, or null if empty.</summary>
    public async Task<int?> GetFitNoteRequiredAfterDaysAsync()
    {
        var input = page.Locator("input.e-numerictextbox[placeholder='Fit Note Required After (Days)']");
        var value = await input.InputValueAsync();
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    /// <summary>Sets the "Return-to-Work Review Required After (Days)" numeric field. Pass null to clear it.</summary>
    public async Task SetReturnToWorkRequiredAfterDaysAsync(int? days)
    {
        var input = page.Locator("input.e-numerictextbox[placeholder='Return-to-Work Review Required After (Days)']");
        await input.FillAsync(days?.ToString() ?? "");
        await page.Keyboard.PressAsync("Tab");
    }

    /// <summary>Returns the current value of the "Return-to-Work Review Required After (Days)" numeric field, or null if empty.</summary>
    public async Task<int?> GetReturnToWorkRequiredAfterDaysAsync()
    {
        var input = page.Locator("input.e-numerictextbox[placeholder='Return-to-Work Review Required After (Days)']");
        var value = await input.InputValueAsync();
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    // ── Save ───────────────────────────────────────────────────────────────────

    public async Task SaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public async Task<bool> HasErrorAsync() =>
        await page.Locator(".alert-danger").First.IsVisibleAsync();
}
