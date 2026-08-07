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

    public async Task<string> GetTitleAsync() =>
        (await page.Locator("h1").First.TextContentAsync())?.Trim() ?? string.Empty;

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
