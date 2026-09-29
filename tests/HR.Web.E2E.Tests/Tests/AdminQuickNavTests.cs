using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class AdminQuickNavTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    private const string TargetFullName = "Sophie Laurent";
    private const string TargetEmployeeNumber = "ACME-007";
    private const string TargetWorkEmail = "sophie.laurent@acme.example";

    private async Task LoginAndOpenPaletteAsync(AdminQuickNavComponent palette, string email = LauraEmail)
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(email);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/employees");
        await palette.OpenAsync();
    }

    [Fact]
    public async Task HrAdmin_SearchesByName_SeesEmployeeRow_AndSelectingLandsOnAdminRecord()
    {
        var palette = new AdminQuickNavComponent(_page);
        await LoginAndOpenPaletteAsync(palette);

        await palette.SearchAsync("Laurent");

        Assert.True(await palette.HasResultAsync(TargetFullName),
            $"Expected a result row for '{TargetFullName}' when searching by surname");
        Assert.True(await palette.HasResultAsync(TargetEmployeeNumber),
            "Expected the employee's number to be shown on the result row");

        await palette.ClickResultAsync(TargetFullName);

        await _page.WaitForURLAsync(
            new Regex(@$"/companies/{AcmeId}/employees/[0-9a-fA-F-]{{36}}(/view)?(\?|#|$)"),
            new() { Timeout = 30_000 });
        Assert.DoesNotContain("/profile", _page.Url);
    }

    [Fact]
    public async Task HrAdmin_SearchesByEmployeeNumber_ReturnsEmployee()
    {
        var palette = new AdminQuickNavComponent(_page);
        await LoginAndOpenPaletteAsync(palette);

        await palette.SearchAsync(TargetEmployeeNumber);

        Assert.True(await palette.HasResultAsync(TargetFullName),
            $"Expected '{TargetFullName}' when searching by employee number '{TargetEmployeeNumber}'");
    }

    [Fact]
    public async Task HrAdmin_SearchesByWorkEmail_ReturnsEmployee()
    {
        var palette = new AdminQuickNavComponent(_page);
        await LoginAndOpenPaletteAsync(palette);

        await palette.SearchAsync(TargetWorkEmail);

        Assert.True(await palette.HasResultAsync(TargetFullName),
            $"Expected '{TargetFullName}' when searching by work email '{TargetWorkEmail}'");
    }

    [Fact]
    public async Task HrAdmin_Leaver_IsHiddenByDefault_ButShownWhenIncludeLeaversTicked()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var dialog = new StartLeavingProcessDialog(_page);
        var palette = new AdminQuickNavComponent(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var leaver = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "QuickNavLeaver", activate: true);
        await empEdit.GoToAsync(AcmeId, leaver.Id);
        await MakeLeaverViaWizardAsync(dialog);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/employees");
        await palette.OpenAsync();

        await palette.SearchAsync(leaver.LastName);
        await palette.AssertNoResultAsync(leaver.FullName);

        await palette.SetIncludeLeaversAsync(true);

        Assert.True(await palette.HasResultAsync(leaver.FullName),
            "Expected the leaver to appear once 'Include leavers / archived employees' is ticked");
    }

    [Theory]
    [InlineData("tom.williams@acme.example")]
    [InlineData("james.okafor@acme.example")]
    [InlineData("marcus.diallo@acme.example")]
    [InlineData("priya.shah@acme.example")]
    public async Task NonHrRole_HasNoTrigger_AndCtrlKIsInert(string email)
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var palette = new AdminQuickNavComponent(_page);

        await login.GoToAsync();
        await login.LoginAsync(email);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}");

        Assert.Equal(0, await palette.Trigger.CountAsync());

        await palette.OpenWithKeyboardAsync();
        await _page.WaitForTimeoutAsync(1_000);

        Assert.Equal(0, await palette.Dialog.CountAsync());
    }

    [Fact]
    public async Task Escape_ClosesPalette_AndReturnsFocusToTrigger()
    {
        var palette = new AdminQuickNavComponent(_page);
        await LoginAndOpenPaletteAsync(palette);

        await palette.SearchAsync("Lau");
        await palette.WaitForResultsSettledAsync();

        await palette.PressEscapeAsync();

        await palette.Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
        await Assertions.Expect(palette.Trigger).ToBeFocusedAsync();
    }

    private async Task MakeLeaverViaWizardAsync(StartLeavingProcessDialog dialog)
    {
        await dialog.OpenAsync();
        await dialog.FillResignationReceivedDateAsync("01/09/2026");
        await dialog.ClickNextAsync();

        var leavingDateRaw = await dialog.GetLeavingDateTextAsync();
        Assert.False(string.IsNullOrWhiteSpace(leavingDateRaw),
            "Expected step 2 to auto-populate a proposed leaving date");
        await dialog.ClickNextAsync();

        await dialog.FillLastWorkingDayAsync(leavingDateRaw!);
        await dialog.ClickNextAsync();

        await dialog.SelectLeavingReasonAsync("Resignation");
        await dialog.ClickNextAsync();

        await dialog.ConfirmAsync();
        Assert.False(await dialog.IsVisibleAsync(),
            "Expected the Start Leaving Process dialog to close after a successful submission");

        await _page.WaitForSelectorAsync("[role='tablist']", new() { Timeout = 20_000 });
    }
}
