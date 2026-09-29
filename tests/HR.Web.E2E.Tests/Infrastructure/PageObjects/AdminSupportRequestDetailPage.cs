using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for HR.Admin.Web's SupportRequestDetails.razor
/// (/customers/{CompanyId}/support-requests/{Id}) — the Admin Portal's status editor for a single
/// support request. Ticket 16: this is where the optimistic-concurrency conflict/reload UI for the
/// support-request status edit lives (SaveStatusAsync / ReloadAfterConflictAsync in the page's
/// @code block) — a bespoke inline reimplementation of HR.Web's shared SaveConflictBanner contract,
/// since HR.Admin.Web cannot reference HR.Web.
/// </summary>
public sealed class AdminSupportRequestDetailPage(IPage page, string baseUrl)
{
    private const string ResolvedSelector = ".details-grid, .dashboard-error";

    private ILocator StatusCombobox => page.Locator(".admin-action-field");

    public async Task GoToAsync(Guid companyId, Guid id)
    {
        await page.GotoAsync($"{baseUrl}/customers/{companyId}/support-requests/{id}");
        await page.WaitForSelectorAsync(ResolvedSelector, new() { Timeout = 20_000 });
    }

    public Task<bool> IsErrorBannerVisibleAsync() =>
        page.Locator(".dashboard-error").IsVisibleAsync();

    public Task<string?> GetTitleAsync() => page.Locator("h2").First.TextContentAsync();

    public async Task<string> GetSelectedStatusAsync()
    {
        var input = StatusCombobox.Locator("input").First;
        await Assertions.Expect(input).Not.ToHaveValueAsync(string.Empty, new() { Timeout = 15_000 });
        return await input.InputValueAsync();
    }

    public Task SelectStatusAsync(string status) =>
        DropDownSelector.SelectAsync(page, StatusCombobox, status);

    private ILocator SaveStatusButton =>
        page.GetByRole(AriaRole.Button, new() { Name = "Save status" });

    public Task<bool> IsSaveDisabledAsync() => SaveStatusButton.IsDisabledAsync();

    public async Task SaveAsync()
    {
        await SaveStatusButton.ClickAsync();
        await page.Locator(".admin-action-success, .save-conflict-banner").First.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
    }

    public async Task SaveExpectingConflictAsync()
    {
        await SaveStatusButton.ClickAsync();
        await ConflictBanner.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
    }

    private ILocator ConflictBanner =>
        page.Locator(".save-conflict-banner[role='alert']")
            .Filter(new() { Has = page.GetByRole(AriaRole.Button, new() { Name = "Reload latest values" }) });

    public Task<bool> IsConflictBannerVisibleAsync() => ConflictBanner.IsVisibleAsync();

    public async Task ClickReloadLatestValuesAsync()
    {
        await ConflictBanner.GetByRole(AriaRole.Button, new() { Name = "Reload latest values" }).ClickAsync();
        await ConflictBanner.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 20_000 });
        await page.WaitForTimeoutAsync(300);
    }

    public Task<bool> IsSuccessMessageVisibleAsync() =>
        page.Locator(".admin-action-success").IsVisibleAsync();

    public Task<string?> GetSuccessMessageAsync() =>
        page.Locator(".admin-action-success").TextContentAsync();

    public Task<bool> IsGlobalErrorVisibleAsync() =>
        page.Locator(".admin-actions-panel .alert-danger").IsVisibleAsync();

    public ILocator BackToSupportRequestsLink =>
        page.GetByRole(AriaRole.Link, new() { Name = "Back to support requests" });

    private ILocator ConversationSection =>
        page.Locator("section.details-panel")
            .Filter(new() { Has = page.GetByRole(AriaRole.Heading, new() { Name = "Conversation", Level = 2, Exact = true }) });

    public ILocator ConversationBodies => ConversationSection.Locator("div.support-response-body");

    public async Task WaitForConversationAsync(int expectedCount = 1)
    {
        await ConversationBodies.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        await Assertions.Expect(ConversationBodies).ToHaveCountAsync(expectedCount, new() { Timeout = 20_000 });
    }
}
