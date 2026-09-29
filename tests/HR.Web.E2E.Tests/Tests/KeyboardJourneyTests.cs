using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class KeyboardJourneyTests(EmployeePersonaFixture fixture)
    : RoleE2ETestBase<EmployeePersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private const string TomEmail = "tom.williams@acme.example";

    private async Task<MyProfilePage> OpenLeaveTabAsync()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);
        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenLeaveTabAsync();
        return profile;
    }

    [Fact]
    public async Task RequestLeave_CanBeCompletedAndSubmittedByKeyboard()
    {
        var profile = await OpenLeaveTabAsync();
        await profile.ClickRequestLeaveAsync();

        // Unique, weekday-only dates per run: this persona's requests persist between runs, so a fixed
        // "today + 2 months" range would overlap an earlier run's request and be rejected by the API.
        var start = DateTime.Today.AddMonths(2).AddDays(Random.Shared.Next(0, 100));
        while (start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) start = start.AddDays(1);
        var end   = start;
        var reason = $"NFR-05 keyboard {Guid.NewGuid():N}".Substring(0, 24);

        await profile.FillLeaveRequestAsync("Annual Leave", start.ToString("dd/MM/yyyy"), end.ToString("dd/MM/yyyy"), reason);

        var submit = _page.GetByRole(AriaRole.Button, new() { Name = "Submit Request" });
        var dialog = _page.GetByRole(AriaRole.Dialog, new() { Name = "Request Leave" });

        var focused = false;
        for (var attempt = 0; attempt < 10 && !focused; attempt++)
        {
            await submit.FocusAsync();
            focused = await submit.EvaluateAsync<bool>("el => el === document.activeElement");
            if (!focused)
                await _page.WaitForTimeoutAsync(200);
        }
        Assert.True(focused, "Expected the 'Submit Request' button to hold keyboard focus before pressing Enter");

        await _page.Keyboard.PressAsync("Enter");

        try
        {
            await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
            await submit.FocusAsync();
            await _page.Keyboard.PressAsync("Enter");
            try
            {
                await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 30_000 });
            }
            catch (TimeoutException)
            {
                var errors = await dialog.Locator(".alert-danger, .validation-message, .text-danger").AllInnerTextsAsync();
                Assert.Fail($"Request Leave dialog stayed open after keyboard submit. Visible errors: [{string.Join(" | ", errors)}]");
            }
        }
    }

    [Fact]
    public async Task RequestLeaveDialog_TrapsFocus_AndEscapeClosesIt()
    {
        var profile = await OpenLeaveTabAsync();
        await profile.ClickRequestLeaveAsync();

        var dialog = _page.GetByRole(AriaRole.Dialog, new() { Name = "Request Leave" });
        await DialogAccessibility.AssertFocusTrappedAsync(_page, dialog);

        await _page.Keyboard.PressAsync("Escape");
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    [Fact]
    public async Task ProfileGrid_ArrowKeys_MoveActiveCellWithinGrid()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);
        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenDocumentsTabAsync();

        var grid = _page.Locator("[data-testid='my-profile-documents-grid-section'] .e-grid").First;
        await grid.WaitForAsync(new() { Timeout = 15_000 });

        var firstCell = grid.Locator(".e-row .e-rowcell").First;
        if (await firstCell.CountAsync() == 0)
            return;

        await firstCell.ClickAsync();
        foreach (var key in new[] { "ArrowRight", "ArrowDown", "ArrowLeft", "ArrowUp" })
        {
            await _page.Keyboard.PressAsync(key);
            var insideGrid = await grid.EvaluateAsync<bool>(
                "el => el.contains(document.activeElement)");
            Assert.True(insideGrid, $"Active element left the grid after pressing {key}.");
        }
    }
}
