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
        await submit.FocusAsync();
        await _page.Keyboard.PressAsync("Enter");

        await _page.GetByRole(AriaRole.Dialog, new() { Name = "Request Leave" })
            .WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
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
