using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// NFR-05: extends DSH-07's dashboard keyboard coverage to core employee workflows — completing the
/// Request Leave form with the keyboard, dialog focus containment + escape, and grid cell/row
/// navigation with the arrow keys. Auth keyboard flow (Tabbing the /login form) lives in
/// <see cref="KeyboardAuthJourneyTests"/> since it makes a real Supabase call.
/// </summary>
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

        var start = DateTime.Today.AddMonths(2);
        var end   = start.AddDays(1);
        var reason = $"NFR-05 keyboard {Guid.NewGuid():N}".Substring(0, 24);

        // FillLeaveRequestAsync drives every field via Tab + typing (the Syncfusion combobox goes
        // through DropDownSelector, which is acceptable per NFR-05 — raw keyboard-driving a
        // Syncfusion combobox is out of scope).
        await profile.FillLeaveRequestAsync("Annual Leave", start.ToString("dd/MM/yyyy"), end.ToString("dd/MM/yyyy"), reason);

        var submit = _page.GetByRole(AriaRole.Button, new() { Name = "Submit Request" });
        var dialog = _page.GetByRole(AriaRole.Dialog, new() { Name = "Request Leave" });

        // FillLeaveRequestAsync's last field commit (Reason, via Tab) fires an async Blazor Server
        // round trip (blur -> ValueChanged -> StateHasChanged) that can still be in flight the
        // instant this method starts — if it re-renders the Submit button element (e.g. its
        // Disabled binding briefly re-evaluating) after FocusAsync() below but before the keyboard
        // Enter is dispatched, Blazor swaps in a fresh DOM node that never actually received focus,
        // silently swallowing the keypress with no popup/error to retry on (a plain single
        // FocusAsync+PressAsync pair has nothing to detect that with). Poll for the button to
        // actually hold document focus before pressing Enter, re-focusing on each attempt so a
        // stale reference from an earlier re-render can't linger.
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
            // The Enter keypress can still land in the same re-render gap described above even
            // after confirming focus (a StateHasChanged triggered by the Enter's own keydown
            // handling, immediately followed by another render swapping the node again) — retry
            // once, re-focusing and re-pressing, before treating this as a genuine failure.
            await submit.FocusAsync();
            await _page.Keyboard.PressAsync("Enter");
            await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
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

        // Tab reaches the grid, then arrow keys move the active cell/row focus inside the Syncfusion
        // grid rather than escaping it.
        var firstCell = grid.Locator(".e-row .e-rowcell").First;
        if (await firstCell.CountAsync() == 0)
            return; // no document rows seeded for Tom — nothing to navigate

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
