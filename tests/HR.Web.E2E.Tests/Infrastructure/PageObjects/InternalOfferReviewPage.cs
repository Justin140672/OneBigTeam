using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the employee's internal job offer: the task shown on their profile Tasks tab
/// (TaskViewDialog + InternalOfferTaskPanel) and the secure review page
/// /companies/{companyId}/internal-offers/{applicationId} (InternalOfferReview.razor) where they accept
/// or decline.
/// </summary>
public sealed class InternalOfferReviewPage(IPage page, string baseUrl)
{
    private static readonly Regex TaskTitlePattern = new("^Review your internal job offer");

    private ILocator Root => page.Locator("[data-testid='internal-offer-review']");
    private ILocator Status => page.Locator("[data-testid='internal-offer-status']");
    private ILocator Unavailable => page.Locator("[data-testid='internal-offer-unavailable']");
    private ILocator Notices => page.Locator("[data-testid='internal-offer-notices']");
    private ILocator Actions => page.Locator("[data-testid='internal-offer-actions']");
    private ILocator AcceptButton => page.Locator("[data-testid='internal-offer-accept']");
    private ILocator DeclineButton => page.Locator("[data-testid='internal-offer-decline']");
    private ILocator DeclinePanel => page.Locator("[data-testid='internal-offer-decline-panel']");
    private ILocator DeclineReason => page.Locator("textarea#internal-offer-decline-reason");
    private ILocator DeclineConfirm => page.Locator("[data-testid='internal-offer-decline-confirm']");
    private ILocator DeclineCancel => page.Locator("[data-testid='internal-offer-decline-cancel']");
    private ILocator Message => page.Locator("[data-testid='internal-offer-message']");
    private ILocator Error => page.Locator("[data-testid='internal-offer-error']");
    private ILocator CannotRespond => page.Locator("[data-testid='internal-offer-cannot-respond']");

    private ILocator Term(string key) => page.Locator($"[data-testid='offer-terms-{key}']");

    public static string DisplayDate(DateOnly date) => date.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);

    public static Regex DisplayDatePattern(DateOnly date) =>
        new($"\\b{date.Day} [A-Za-z]{{3,4}}\\.? {date.Year}\\b");

    public async Task GoToAsync(Guid companyId, Guid applicationId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/internal-offers/{applicationId}");
        await WaitForLoadedAsync();
    }

    public Task WaitForLoadedAsync() =>
        Status.Or(Unavailable).WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });

    public Task ExpectUnavailableAsync() =>
        Assertions.Expect(Unavailable).ToBeVisibleAsync(new() { Timeout = 20_000 });

    public async Task ExpectUnavailableWithNoOfferDetailsAsync()
    {
        await ExpectUnavailableAsync();
        await Assertions.Expect(Status).ToHaveCountAsync(0);
        await Assertions.Expect(Term("salary")).ToHaveCountAsync(0);
        await Assertions.Expect(Actions).ToHaveCountAsync(0);
    }

    public async Task ExpectNoticesAsync()
    {
        await Assertions.Expect(Notices).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(Notices).ToContainTextAsync("No new employee record is created");
        await Assertions.Expect(Notices).ToContainTextAsync("employee number does not change");
        await Assertions.Expect(Notices).ToContainTextAsync("original start date and continuous-service date are unchanged");
        await Assertions.Expect(Notices).ToContainTextAsync("no new-starter onboarding is triggered");
    }

    public Task ExpectStatusAsync(int version, string statusLabel) =>
        Assertions.Expect(Status).ToContainTextAsync(
            new Regex($"Version {version}\\b.*Status:\\s*{Regex.Escape(statusLabel)}", RegexOptions.Singleline),
            new() { Timeout = 20_000 });

    public Task ExpectTermAsync(string key, string expectedText) =>
        Assertions.Expect(Term(key)).ToContainTextAsync(expectedText, new() { Timeout = 10_000 });

    public Task ExpectTermAsync(string key, Regex expected) =>
        Assertions.Expect(Term(key)).ToContainTextAsync(expected, new() { Timeout = 10_000 });

    public Task ExpectTermVisibleAsync(string key) =>
        Assertions.Expect(Term(key)).ToBeVisibleAsync(new() { Timeout = 10_000 });

    public async Task ExpectTermPopulatedAsync(string key)
    {
        await Assertions.Expect(Term(key)).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(Term(key)).Not.ToHaveTextAsync("Not specified", new() { Timeout = 10_000 });
    }

    public Task ExpectTermExactAsync(string key, string expectedText) =>
        Assertions.Expect(Term(key)).ToHaveTextAsync(expectedText, new() { Timeout = 10_000 });

    public Task ExpectRespondedAtVisibleAsync(bool visible) =>
        visible
            ? Assertions.Expect(Term("responded-at")).ToBeVisibleAsync(new() { Timeout = 15_000 })
            : Assertions.Expect(Term("responded-at")).ToHaveCountAsync(0, new() { Timeout = 10_000 });

    public Task ExpectActionsVisibleAsync() =>
        Assertions.Expect(Actions).ToBeVisibleAsync(new() { Timeout = 15_000 });

    public Task ExpectNoActionsAsync() =>
        Assertions.Expect(Actions).ToHaveCountAsync(0, new() { Timeout = 15_000 });

    public Task ExpectCannotRespondAsync(string expectedText) =>
        Assertions.Expect(CannotRespond).ToContainTextAsync(expectedText, new() { Timeout = 15_000 });

    public async Task AcceptAsync()
    {
        await AcceptButton.ClickAsync();
        await Assertions.Expect(Message).ToContainTextAsync("You have accepted this offer", new() { Timeout = 20_000 });
        await Assertions.Expect(Actions).ToHaveCountAsync(0, new() { Timeout = 15_000 });
    }

    public async Task OpenDeclinePanelAsync()
    {
        await DeclineButton.ClickAsync();
        await Assertions.Expect(DeclinePanel).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    public async Task CancelDeclineAsync()
    {
        await DeclineCancel.ClickAsync();
        await Assertions.Expect(DeclinePanel).ToHaveCountAsync(0, new() { Timeout = 10_000 });
        await Assertions.Expect(Actions).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    public async Task DeclineAsync(string? reason)
    {
        await OpenDeclinePanelAsync();
        if (reason is not null)
        {
            await DeclineReason.FillAsync(reason);
            await page.Keyboard.PressAsync("Tab");
            await Assertions.Expect(DeclineReason).ToHaveValueAsync(reason, new() { Timeout = 10_000 });
        }

        await DeclineConfirm.ClickAsync();
        await Assertions.Expect(Message).ToContainTextAsync("You have declined this offer", new() { Timeout = 20_000 });
        await Assertions.Expect(DeclinePanel).ToHaveCountAsync(0, new() { Timeout = 15_000 });
        await Assertions.Expect(Actions).ToHaveCountAsync(0, new() { Timeout = 15_000 });
    }

    public Task ExpectNoErrorAsync() =>
        Assertions.Expect(Error).ToHaveCountAsync(0, new() { Timeout = 5_000 });


    private ILocator TaskRows(string statusClass) =>
        page.Locator(".e-row")
            .Filter(new() { Has = page.GetByRole(AriaRole.Button, new() { NameRegex = TaskTitlePattern }) })
            .Filter(new() { Has = page.Locator($".task-status-badge--{statusClass}") });

    public async Task GoToTasksTabAsync(Guid companyId, Guid employeeId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/{employeeId}/profile?tab=tasks");
        try
        {
            await page.Locator(".hr-loading").WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 20_000 });
        }
        catch (TimeoutException)
        {
        }
    }

    public Task ExpectOfferTaskRowsAsync(string statusClass, int count) =>
        Assertions.Expect(TaskRows(statusClass)).ToHaveCountAsync(count, new() { Timeout = 30_000 });

    public async Task ExpectOfferTaskTitleAsync(string jobTitleFragment)
    {
        var title = TaskRows("open").First.Locator(".task-title");
        await Assertions.Expect(title).ToContainTextAsync(
            $"Review your internal job offer — {jobTitleFragment}", new() { Timeout = 20_000 });
    }

    public async Task OpenOfferTaskAsync()
    {
        var row = TaskRows("open").First;
        await row.Locator(".task-title-link").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await row.Locator(".task-title-link").ClickAsync();

        await new TaskViewPage(page, baseUrl).WaitForLoadedAsync();
        await Assertions.Expect(TaskPanel).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    private ILocator TaskPanel =>
        page.Locator("[role='dialog'].task-view-dialog [data-testid='internal-offer-task-panel']");

    public async Task ClickReviewOfferFromTaskAsync(Guid applicationId)
    {
        await TaskPanel.Locator("[data-testid='internal-offer-review-btn']")
            .ClickUntilUrlAsync(page, u => u.Contains($"/internal-offers/{applicationId}"));
        await WaitForLoadedAsync();
    }
}
