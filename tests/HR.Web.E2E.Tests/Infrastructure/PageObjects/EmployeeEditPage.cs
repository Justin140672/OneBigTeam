using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the employee edit/create page.
/// Covers the new-employee form and the Employment tab of existing employees.
/// Routes: /companies/{id}/employees/new  and  /companies/{id}/employees/{id}
/// </summary>
public sealed class EmployeeEditPage(IPage page, string baseUrl)
{
    public async Task GoToNewAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/new");
        // Wait for Syncfusion to initialise — span[role='combobox'] only appears after
        // Blazor's interactive render, ensuring the form's event handlers are wired up.
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    public async Task GoToAsync(Guid companyId, Guid employeeId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/{employeeId}");
        // span[role='combobox'] (SfDropDownList) only appears after Blazor's interactive
        // render, confirming the circuit is connected and event handlers are wired up.
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    // ── New Employee — Personal Information ───────────────────────────────────

    public async Task FillFirstNameAsync(string value) =>
        await page.GetByPlaceholder("First name", new() { Exact = true }).FillAsync(value);

    public async Task FillLastNameAsync(string value) =>
        await page.GetByPlaceholder("Last name").FillAsync(value);

    public async Task FillWorkEmailAsync(string value) =>
        await page.GetByPlaceholder("work@company.com").FillAsync(value);

    public async Task FillStartDateAsync(string ddMMyyyy)
    {
        var inputs = page.Locator(".e-date-wrapper input.e-input");
        // The start date picker is the first date input on the new employee form.
        await inputs.First.ClickAsync();
        await inputs.First.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillDateOfBirthAsync(string ddMMyyyy)
    {
        // Date of birth is the second date picker on the new employee form.
        var inputs = page.Locator(".e-date-wrapper input.e-input");
        await inputs.Nth(1).ClickAsync();
        await inputs.Nth(1).FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    /// <summary>Selects a value from a Syncfusion SfDropDownList identified by nearby label text.</summary>
    public async Task SelectDropdownAsync(string labelText, string optionText)
    {
        var group = page.Locator(".col-md-6, .col-md-4")
            .Filter(new() { HasText = labelText })
            .First;
        await group.Locator("span[role='combobox']").First.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        await page.Locator(".e-popup.e-ddl .e-list-item")
            .Filter(new() { HasText = optionText })
            .First
            .ClickAsync();
    }

    // ── Employment Tab ─────────────────────────────────────────────────────────

    public async Task OpenEmploymentTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Employment" }).ClickAsync();
        // Wait for the Employment-tab-specific heading — the generic .card-header selector
        // would resolve immediately against the Details tab's already-rendered card headers.
        await page.WaitForSelectorAsync(".card-header:has-text('Employment Details')", new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Selects a manager from the Manager dropdown on the Employment tab.
    /// </summary>
    public async Task SelectManagerAsync(string managerNameFragment)
    {
        var managerGroup = page.Locator(".col-md-4, .col-12")
            .Filter(new() { HasText = "Manager" })
            .First;
        await managerGroup.Locator("span[role='combobox']").First.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        // Wait for list items to be populated before trying to click one — the popup can
        // appear before Syncfusion binds the DataSource when AllowFiltering is enabled.
        await page.WaitForSelectorAsync(".e-popup.e-ddl .e-list-item", new() { Timeout = 15_000 });
        await page.Locator(".e-popup.e-ddl .e-list-item")
            .Filter(new() { HasText = managerNameFragment })
            .First
            .ClickAsync();
    }

    public async Task ClickSaveChangesAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save Changes" }).ClickAsync();
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    // ── Save (new employee form) ───────────────────────────────────────────────

    public async Task SaveNewEmployeeAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        // Navigates to the employee list on success.
        await page.WaitForURLAsync("**/employees", new() { Timeout = 20_000 });
        // With prerender:false the circuit connects after navigation, wait for the grid.
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task<bool> HasProbationSummaryAsync() =>
        await page.Locator("[data-testid='probation-summary']").IsVisibleAsync();

    public async Task<bool> HasErrorAsync() =>
        await page.Locator(".alert-danger, .validation-message").First.IsVisibleAsync();

    // ── Probation Tab ──────────────────────────────────────────────────────────

    public async Task OpenProbationTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Probation" }).ClickAsync();
        // Wait for the tab content to render — either a card or "No probation record" alert.
        await page.WaitForSelectorAsync(".card, .alert-secondary", new() { Timeout = 15_000 });
    }

    /// <summary>Returns true if the probation period summary panel (progress bar card) is visible.</summary>
    public async Task<bool> HasProbationPeriodSummaryPanelAsync() =>
        await page.Locator(".progress").IsVisibleAsync();

    /// <summary>Returns true if the Syncfusion review history grid is visible on the Probation tab.</summary>
    public async Task<bool> HasProbationReviewsGridAsync() =>
        await page.Locator(".e-grid").IsVisibleAsync();

    /// <summary>Returns the text of the probation status badge on the Probation tab summary panel.</summary>
    public async Task<string?> GetProbationStatusBadgeTextAsync()
    {
        var badge = page.Locator(".card .badge").First;
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    /// <summary>
    /// Returns the status badge text for the first review row in the review history grid
    /// whose ReviewType cell contains <paramref name="reviewTypeFragment"/>.
    /// </summary>
    public async Task<string?> GetReviewStatusInGridAsync(string reviewTypeFragment)
    {
        await page.WaitForSelectorAsync(".e-grid .e-row", new() { Timeout = 10_000 });

        var rows = await page.Locator(".e-grid .e-row").AllAsync();
        foreach (var row in rows)
        {
            var text = await row.TextContentAsync();
            if (text?.Contains(reviewTypeFragment, StringComparison.OrdinalIgnoreCase) != true)
                continue;

            // Status badge is within the row — grab the first badge element.
            var badge = row.Locator(".badge").First;
            if (await badge.IsVisibleAsync())
                return (await badge.TextContentAsync())?.Trim();
        }

        return null;
    }
}
