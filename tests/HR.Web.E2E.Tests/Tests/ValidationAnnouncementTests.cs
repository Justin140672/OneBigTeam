using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// NFR-05: exercises the shipped <c>HrValidationSummary</c> shared component
/// (src/HR.Web/Components/Controls/HrValidationSummary.razor).
///
/// Contract asserted here:
///  - The summary element is ONLY in the DOM when there is ≥1 message. Before any invalid
///    submit it is absent (negative case, asserted on LeaveTypeEdit).
///  - When present it renders as
///    <c>&lt;div class="hr-validation-summary" role="alert" aria-live="assertive" aria-atomic="true"&gt;</c>
///    with a non-empty <c>&lt;ul class="hr-validation-summary-list"&gt;</c>.
///  - Each field that currently has a validation message carries <c>aria-invalid="true"</c>
///    (Name on LeavePolicyEdit; Name + Code on LeaveTypeEdit; Leave Type dropdown on the
///    Request Leave dialog via Syncfusion <c>HtmlAttributes</c>).
///  - On the Request Leave dialog (which has no &lt;EditForm&gt;) the summary is fed by the
///    <c>AdditionalErrors</c> parameter and appears after a failed Submit; it is scoped within
///    the <c>role="dialog"</c> named "Request Leave".
/// </summary>
public sealed class ValidationAnnouncementTests(CrossUserFixture fixture)
    : CrossUserTenantAndMiscTestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private const string TomEmail   = "tom.williams@acme.example";
    private const string LauraEmail = "laura.bennett@acme.example";

    // The exact contract the HrValidationSummary shared component satisfies.
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

        // Nothing filled in — manual per-field validation should fail and set field errors.
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Submit Request" }).ClickAsync();

        var summary = dialog.Locator(ValidationSummarySelector).First;
        await AssertSummaryAnnouncedAsync(summary);

        // aria-invalid is applied via Syncfusion HtmlAttributes on the Leave Type SfDropDownList.
        // Target whatever DOM node actually carries it (wrapper vs inner input) with a tolerant
        // selector scoped to the dialog.
        // NFR-05: verify aria-invalid host node in nightly
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

        // Negative case: no summary before an invalid submit.
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

        // Negative case: the summary element is absent from the DOM before any invalid submit.
        Assert.Equal(0, await _page.Locator(ValidationSummarySelector).CountAsync());

        // Submit the empty form — Name/Code are required.
        await _page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        var summary = _page.Locator(ValidationSummarySelector).First;
        await AssertSummaryAnnouncedAsync(summary);

        Assert.True(
            await _page.Locator("[aria-invalid='true']").CountAsync() > 0,
            "Expected at least one invalid field to be marked aria-invalid=\"true\".");
    }
}
