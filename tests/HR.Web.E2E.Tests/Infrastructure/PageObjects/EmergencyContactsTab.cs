using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the Emergency Contacts tab on the self-service My Profile page.
/// Selectors derived from MyProfileEmergencyContactsTab.razor.
/// </summary>
public sealed class EmergencyContactsTab(IPage page)
{
    public async Task WaitForLoadAsync() =>
        await page.WaitForSelectorAsync(".ec-card, .alert", new() { Timeout = 15_000 });

    // ── Add Contact ────────────────────────────────────────────────────────────

    /// <summary>Clicks the "Add Contact" button in the card header (or the empty-state button).</summary>
    public async Task ClickAddContactAsync()
    {
        // Try the header "Add Contact" button first; fall back to the empty-state button.
        var btn = page.GetByRole(AriaRole.Button, new() { Name = "Add Contact" }).First;
        await btn.ClickAsync();
        // Wait for the inline form to appear.
        await page.WaitForSelectorAsync("input[placeholder='Full name']", new() { Timeout = 10_000 });
    }

    public async Task FillContactNameAsync(string name) =>
        await page.GetByPlaceholder("Full name").FillAsync(name);

    public async Task FillContactRelationshipAsync(string relationship) =>
        await page.GetByPlaceholder("e.g. Spouse, Parent, Sibling").FillAsync(relationship);

    public async Task FillContactPhoneAsync(string phone) =>
        await page.GetByPlaceholder("e.g. 07700 900000").Last.FillAsync(phone);

    public async Task FillContactEmailAsync(string email) =>
        await page.GetByPlaceholder("e.g. name@example.com").FillAsync(email);

    /// <summary>Submits the add-contact form and waits for the success banner.</summary>
    public async Task SaveContactAsync()
    {
        // There may be multiple "Add Contact" buttons; the save button inside the form has the primary class.
        var saveBtn = page.Locator("button.e-primary, button[type='submit']")
            .Filter(new() { HasText = "Add Contact" })
            .Last;
        await saveBtn.ClickAsync();
        await page.WaitForSelectorAsync(".ec-success-banner", new() { Timeout = 15_000 });
    }

    // ── Contact list ───────────────────────────────────────────────────────────

    public async Task<bool> HasContactAsync(string nameFragment) =>
        await page.Locator(".ec-contact-name")
            .Filter(new() { HasText = nameFragment })
            .First
            .IsVisibleAsync();

    public async Task<IReadOnlyList<string>> GetContactNamesAsync()
    {
        var items = await page.Locator(".ec-contact-name").AllAsync();
        var names = new List<string>();
        foreach (var item in items)
            names.Add((await item.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    public async Task<bool> IsEmptyStateVisibleAsync() =>
        await page.GetByText("No emergency contacts added yet.").IsVisibleAsync();

    public async Task<bool> IsSuccessBannerVisibleAsync() =>
        await page.Locator(".ec-success-banner").IsVisibleAsync();

    // ── Edit / Remove ──────────────────────────────────────────────────────────

    /// <summary>Clicks the edit icon for the contact card whose name contains <paramref name="nameFragment"/>.</summary>
    public async Task ClickEditContactAsync(string nameFragment)
    {
        var card = page.Locator(".ec-contact-card")
            .Filter(new() { HasText = nameFragment })
            .First;
        await card.Locator("button[title='Edit']").ClickAsync();
        await page.WaitForSelectorAsync("input[placeholder='Full name']", new() { Timeout = 10_000 });
    }

    /// <summary>Clicks the remove icon for the contact card whose name contains <paramref name="nameFragment"/>.</summary>
    public async Task ClickRemoveContactAsync(string nameFragment)
    {
        var card = page.Locator(".ec-contact-card")
            .Filter(new() { HasText = nameFragment })
            .First;
        await card.Locator("button[title='Remove']").ClickAsync();
        await page.WaitForSelectorAsync(".ec-success-banner", new() { Timeout = 15_000 });
    }
}
