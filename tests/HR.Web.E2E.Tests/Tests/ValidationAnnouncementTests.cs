using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class ValidationAnnouncementTests(CrossUserFixture fixture)
    : RoleE2ETestBase<CrossUserFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private const string TomEmail   = "tom.williams@acme.example";
    private const string LauraEmail = "laura.bennett@acme.example";

    private const string ValidationSummarySelector = "div.hr-validation-summary[role='alert']";

    private async Task LoginAsync(string email)
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(email);
    }

    private static async Task AssertSummaryAnnouncedAsync(ILocator summary)
    {
        await summary.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await Assertions.Expect(summary).ToHaveAttributeAsync("aria-live", "assertive");
        await Assertions.Expect(summary).ToHaveAttributeAsync("aria-atomic", "true");
        Assert.False(
            string.IsNullOrWhiteSpace((await summary.InnerTextAsync())?.Trim()),
            "Expected the validation summary to list at least one error message.");
        Assert.True(
            await summary.Locator("ul.hr-validation-summary-list li").CountAsync() > 0,
            "Expected the validation summary to render at least one <li> message.");
    }

    [Fact]
    public async Task RequestLeaveDialog_InvalidSubmit_AnnouncesValidationSummary_AndMarksInvalidField()
    {
        await LoginAsync(TomEmail);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);
        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenLeaveTabAsync();
        await profile.ClickRequestLeaveAsync();

        var dialog = _page.GetByRole(AriaRole.Dialog, new() { Name = "Request Leave" });

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Submit Request" }).ClickAsync();

        var summary = dialog.Locator(ValidationSummarySelector).First;
        await AssertSummaryAnnouncedAsync(summary);

        Assert.True(
            await dialog.Locator("[aria-invalid='true']").CountAsync() > 0,
            "Expected at least one field in the dialog to be marked aria-invalid=\"true\".");
    }

    [Fact]
    public async Task LeavePolicyEdit_InvalidSubmit_AnnouncesValidationSummary_AndMarksNameInvalid()
    {
        await LoginAsync(LauraEmail);
        var edit = new LeavePolicyEditPage(_page, _fixture.WebBaseUrl);
        await edit.GoToNewAsync(AcmeId);

        Assert.Equal(0, await _page.Locator(ValidationSummarySelector).CountAsync());

        await _page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        var summary = _page.Locator(ValidationSummarySelector).First;
        await AssertSummaryAnnouncedAsync(summary);

        Assert.True(
            await _page.Locator("[aria-invalid='true']").CountAsync() > 0,
            "Expected the Name field to be marked aria-invalid=\"true\".");
    }

    [Fact]
    public async Task LeaveTypeEdit_InvalidSubmit_AnnouncesValidationSummary_AndMarksInvalidFields()
    {
        await LoginAsync(LauraEmail);
        var edit = new LeaveTypeEditPage(_page, _fixture.WebBaseUrl);
        await edit.GoToNewAsync(AcmeId);

        Assert.Equal(0, await _page.Locator(ValidationSummarySelector).CountAsync());

        await _page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        var summary = _page.Locator(ValidationSummarySelector).First;
        await AssertSummaryAnnouncedAsync(summary);

        Assert.True(
            await _page.Locator("[aria-invalid='true']").CountAsync() > 0,
            "Expected at least one invalid field to be marked aria-invalid=\"true\".");
    }
}
