using System.Net.Http.Json;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class ExpiredTrialCompleteProfileTests(ParallelBlankPersonaFixture fixture)
    : RoleE2ETestBase<ParallelBlankPersonaFixture>(fixture)
{
    private const string DevPersonaPassword = "Dev-Only-Password-1!";

    private const string DialogSeenFlag = "__completionDialogSeen";

    private async Task<string> SignUpActivateAndExpireFreshCompanyAsync()
    {
        using var http = new HttpClient { BaseAddress = new Uri(_fixture.ApiBaseUrl) };

        var suffix = Guid.NewGuid().ToString("N")[..10];
        var email = $"e2e-expired-trial-{suffix}@example.com";

        var signUpResponse = await http.PostAsJsonAsync("/api/signup", new
        {
            CompanyName = $"E2E Expired Trial Co {suffix}",
            AdminFirstName = "Placeholder",
            AdminLastName = "Admin",
            AdminEmail = email,
            Password = DevPersonaPassword,
        });
        Assert.True(signUpResponse.IsSuccessStatusCode);

        var signUp = await signUpResponse.Content.ReadFromJsonAsync<SignUpResult>();
        Assert.NotNull(signUp);

        var activateResponse = await http.PostAsJsonAsync(
            "/api/dev/activate-company", new { CompanyId = signUp!.CompanyId });
        Assert.Equal(System.Net.HttpStatusCode.NoContent, activateResponse.StatusCode);

        var expireResponse = await http.PostAsJsonAsync(
            "/api/dev/expire-company-trial", new { CompanyId = signUp.CompanyId });
        Assert.Equal(System.Net.HttpStatusCode.NoContent, expireResponse.StatusCode);

        return email;
    }

    private async Task LoginWithDialogWatcherAsync(string email)
    {
        await _page.AddInitScriptAsync($$"""
            window.{{DialogSeenFlag}} = false;
            new MutationObserver(() => {
                if (document.querySelector('.employee-completion-dialog')) window.{{DialogSeenFlag}} = true;
            }).observe(document, { childList: true, subtree: true });
            """);

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(email, DevPersonaPassword);
        await _page.WaitForSelectorAsync(".app-shell", new() { Timeout = 30_000 });
    }

    [Fact]
    public async Task ExpiredTrialAdmin_WithIncompleteProfile_NeverSeesCompleteProfileDialog()
    {
        var email = await SignUpActivateAndExpireFreshCompanyAsync();

        await LoginWithDialogWatcherAsync(email);

        Assert.False(await _page.Locator(".employee-completion-dialog").IsVisibleAsync());
        Assert.False(await _page.EvaluateAsync<bool>($"window.{DialogSeenFlag} === true"),
            "The Complete Profile dialog must never render for a read-only company, not even briefly.");
    }

    [Fact]
    public async Task ExpiredTrialAdmin_SeesSubscriptionAlert_AndCanOpenSubscriptionAndStartCheckout()
    {
        var email = await SignUpActivateAndExpireFreshCompanyAsync();

        await LoginWithDialogWatcherAsync(email);

        var alert = await WaitForExpiredAlertAsync();
        Assert.Equal("Your free trial has ended", (await alert.GetByTestId("subscription-alert-heading").InnerTextAsync()).Trim());
        Assert.Contains("read-only", await alert.InnerTextAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Start subscription", (await alert.GetByTestId("subscription-alert-action").InnerTextAsync()).Trim());
        Assert.Equal(0, await alert.GetByTestId("subscription-alert-guidance").CountAsync());

        await alert.GetByTestId("subscription-alert-action").ClickAsync();
        await AssertSubscriptionPageOpenedAsync();
    }

    [Fact]
    public async Task ExpiredTrialAdmin_CanActivateSubscriptionAlertActionWithKeyboard()
    {
        var email = await SignUpActivateAndExpireFreshCompanyAsync();

        await LoginWithDialogWatcherAsync(email);

        var alert = await WaitForExpiredAlertAsync();
        await alert.GetByTestId("subscription-alert-action").FocusAsync();
        await _page.Keyboard.PressAsync("Enter");

        await AssertSubscriptionPageOpenedAsync();
    }

    [Fact]
    public async Task ExpiredTrialAdmin_AlertRemainsVisibleAfterNavigatingToAnotherPage()
    {
        var email = await SignUpActivateAndExpireFreshCompanyAsync();

        await LoginWithDialogWatcherAsync(email);
        await WaitForExpiredAlertAsync();

        await _page.GetByText("Company Profile & Addresses").First.ClickAsync();
        await _page.WaitForURLAsync(url => url.Contains("/edit"), new() { Timeout = 15_000 });

        await WaitForExpiredAlertAsync();
    }

    [Fact]
    public async Task ExpiredTrialAdmin_AlertReflowsOnNarrowViewportWithoutHorizontalScroll()
    {
        var email = await SignUpActivateAndExpireFreshCompanyAsync();

        await _page.SetViewportSizeAsync(375, 800);
        await LoginWithDialogWatcherAsync(email);

        var alert = await WaitForExpiredAlertAsync();
        var action = alert.GetByTestId("subscription-alert-action");
        await action.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        var overflows = await _page.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth > document.documentElement.clientWidth");
        Assert.False(overflows, "The subscription alert must not cause horizontal scrolling at 375px.");

        var box = await action.BoundingBoxAsync();
        Assert.NotNull(box);
        Assert.True(box!.Height >= 44);
        Assert.True(box.X >= 0 && box.X + box.Width <= 375);
    }

    [Fact]
    public async Task ExpiredTrialAdmin_AlertStateHasNoSeriousAccessibilityViolations()
    {
        var email = await SignUpActivateAndExpireFreshCompanyAsync();

        await LoginWithDialogWatcherAsync(email);
        await WaitForExpiredAlertAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await AccessibilityScan.AssertNoSeriousViolationsAsync(_page, "expired-trial subscription alert");
    }

    private async Task<ILocator> WaitForExpiredAlertAsync()
    {
        var alert = _page.GetByTestId("subscription-alert");
        await alert.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        Assert.Equal("expired", await alert.GetAttributeAsync("data-variant"));
        return alert;
    }

    private async Task AssertSubscriptionPageOpenedAsync()
    {
        await _page.WaitForURLAsync(url => url.Contains("/subscription"), new() { Timeout = 15_000 });
        await _page.WaitForSelectorAsync(".card-header h5", new() { Timeout = 20_000 });

        var billing = new SubscriptionBillingPage(_page, _fixture.WebBaseUrl);
        Assert.True(await billing.HasStartSubscriptionButtonAsync());
        Assert.False(await _page.Locator(".employee-completion-dialog").IsVisibleAsync());
    }

    private sealed record SignUpResult(Guid UserId, Guid CompanyId);
}
