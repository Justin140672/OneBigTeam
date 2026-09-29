using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class ResetPasswordPage(IPage page, string baseUrl)
{
    public Task GoToWithoutTokenAsync() =>
        page.GotoAsync($"{baseUrl}/reset-password");

    public async Task<bool> IsInvalidLinkMessageVisibleAsync()
    {
        var message = page.GetByText("This password reset link is no longer valid. Please request a new one.");

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await message.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = attempt < 3 ? 10_000 : 20_000 });
                return true;
            }
            catch (TimeoutException) when (attempt < 3)
            {
                await page.ReloadAsync();
            }
            catch (TimeoutException)
            {
                return false;
            }
        }

        return false;
    }

    public Task ClickBackToForgotPasswordAsync() =>
        page.GetByRole(AriaRole.Link, new() { Name = "Back to Forgot Password" }).ClickAsync();
}
