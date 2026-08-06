using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies client-side and server-side validation on the leave request form:
/// - End date before start date is rejected.
/// - A leave type must be selected.
/// - Start date is required.
/// </summary>
[Collection("E2E")]
public sealed class LeaveFormValidationTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string TomEmail = "tom.williams@acme.example";

    [Fact]
    public async Task SubmitLeaveRequest_WithEndDateBeforeStartDate_IsRejected()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenLeaveTabAsync();

        await profile.ClickRequestLeaveAsync();

        // Fill the form with an end date that is before the start date.
        await profile.FillLeaveRequestAsync(
            leaveTypeName: "Annual Leave",
            startDate:     "20/03/2026",
            endDate:       "15/03/2026", // end is before start — invalid
            reason:        "E2E-VALIDATION-END-BEFORE-START");

        await _page.GetByRole(AriaRole.Button, new() { Name = "Submit Request" }).ClickAsync();

        // Wait for the inline validation error to appear.
        await _page.WaitForSelectorAsync(".invalid-feedback", new() { Timeout = 5_000 });

        // Dialog must NOT close on invalid input.
        Assert.True(await _page.GetByRole(AriaRole.Dialog, new() { Name = "Request Leave" }).IsVisibleAsync(),
            "The leave request dialog should remain open when end date is before start date");

        // An inline validation error must be visible.
        Assert.True(await _page.Locator(".invalid-feedback").First.IsVisibleAsync(),
            "Expected a validation error when end date is before start date");
    }

    [Fact]
    public async Task SubmitLeaveRequest_WithNoLeaveType_IsRejected()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenLeaveTabAsync();

        await profile.ClickRequestLeaveAsync();

        // Fill dates but skip selecting a leave type.
        var dialog = _page.GetByRole(AriaRole.Dialog, new() { Name = "Request Leave" });
        var dateInputs = dialog.Locator(".e-date-wrapper input.e-input");

        await dateInputs.Nth(0).ClickAsync();
        await dateInputs.Nth(0).FillAsync("22/04/2026");
        await _page.Keyboard.PressAsync("Tab");

        await dateInputs.Nth(1).ClickAsync();
        await dateInputs.Nth(1).FillAsync("24/04/2026");
        await _page.Keyboard.PressAsync("Tab");

        await _page.GetByRole(AriaRole.Button, new() { Name = "Submit Request" }).ClickAsync();

        // Wait for the inline validation error to appear.
        await _page.WaitForSelectorAsync(".invalid-feedback", new() { Timeout = 5_000 });

        // Dialog should remain open.
        Assert.True(await _page.GetByRole(AriaRole.Dialog, new() { Name = "Request Leave" }).IsVisibleAsync(),
            "The leave request dialog should remain open when no leave type is selected");

        // An inline validation error must be visible.
        Assert.True(await _page.Locator(".invalid-feedback").First.IsVisibleAsync(),
            "Expected a validation error when no leave type is selected");
    }

    [Fact]
    public async Task SubmitLeaveRequest_WithNoStartDate_IsRejected()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenLeaveTabAsync();

        await profile.ClickRequestLeaveAsync();

        // Select leave type but leave dates empty.
        var dialog = _page.GetByRole(AriaRole.Dialog, new() { Name = "Request Leave" });
        await DropDownSelector.SelectAsync(_page, dialog, "Annual Leave");

        await _page.GetByRole(AriaRole.Button, new() { Name = "Submit Request" }).ClickAsync();

        // Wait for the inline validation error to appear.
        await _page.WaitForSelectorAsync(".invalid-feedback", new() { Timeout = 5_000 });

        // Dialog should remain open.
        Assert.True(await _page.GetByRole(AriaRole.Dialog, new() { Name = "Request Leave" }).IsVisibleAsync(),
            "The leave request dialog should remain open when no start date is provided");

        // An inline validation error for the missing date must be visible.
        Assert.True(await _page.Locator(".invalid-feedback").First.IsVisibleAsync(),
            "Expected a validation error when no start date is provided");
    }
}
