using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for SupportRequestDetail.razor (/companies/{companyId}/support/{id}) — request
/// details, diagnostics block, attachments, response thread and the reply form.
/// </summary>
public sealed class SupportRequestDetailPage(IPage page, string baseUrl)
{
    private ILocator ConversationCard => page.Locator(".card").Filter(new() { HasText = "Conversation" });

    public async Task GoToAsync(Guid companyId, Guid id)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/support/{id}");
        await page.WaitForSelectorAsync("h1", new() { Timeout = 20_000 });
    }

    /// <summary>
    /// The detail page's own h1 (the support request's title). Callers that just navigated here
    /// via a client-side transition (e.g. HelpFeedbackPage.SubmitAsync, which only waits for the
    /// URL to change) can race a shared-layout h1 that's still showing the previous page's
    /// heading (e.g. "Help &amp; Feedback") for a render pass or two before this page's own
    /// content — including its title — actually mounts. Wait for the Conversation card (content
    /// unique to this page) to appear first, so the h1 read afterwards is guaranteed to be this
    /// page's own, not a stale layout leftover.
    /// </summary>
    public async Task<string> GetTitleAsync()
    {
        await ConversationCard.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        return (await page.Locator("h1").First.TextContentAsync())?.Trim() ?? string.Empty;
    }

    public Task<bool> IsDiagnosticsSectionVisibleAsync() =>
        page.Locator(".card").Filter(new() { HasText = "Diagnostics" }).IsVisibleAsync();

    public Task FillReplyAsync(string text) =>
        ConversationCard.GetByPlaceholder("Write a reply…").FillAsync(text);

    public async Task SendReplyAsync()
    {
        await ConversationCard.GetByRole(AriaRole.Button, new() { Name = "Send Reply" }).ClickAsync();
        // The form clears and the thread reloads after a successful reply; wait for the
        // textbox to go back to empty as a signal that SubmitReplyAsync completed.
        await Assertions.Expect(ConversationCard.GetByPlaceholder("Write a reply…")).ToHaveValueAsync("");
    }

    public async Task<bool> HasThreadEntryAsync(string textFragment)
    {
        return await page.Locator(".support-thread-item")
            .Filter(new() { HasText = textFragment })
            .First
            .WaitUntilVisibleAsync();
    }

    public Task<bool> HasReplyErrorAsync() =>
        ConversationCard.Locator(".alert-danger, .validation-message").First.IsVisibleAsync();
}
