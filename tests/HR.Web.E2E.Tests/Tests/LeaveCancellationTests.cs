using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies that an employee can cancel a pending leave request and that the
/// request is removed from the table — balance is unaffected because the request
/// was never approved.
/// </summary>
public sealed class LeaveCancellationTests(EmployeePersonaFixture fixture) : RoleE2ETestBase<EmployeePersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string TomEmail = "tom.williams@acme.example";

    // Dates chosen to avoid collisions with other E2E tests.
    private const string StartDate = "12/01/2026";
    private const string EndDate   = "16/01/2026"; // Mon–Fri = 5 working days

    [Fact]
    public async Task CancellingPendingLeaveRequest_RemovesItFromTheTable()
    {
        var reason = $"E2E-CANCEL-{Guid.NewGuid():N}";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Tom and record his current annual leave balance ──
        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenLeaveTabAsync();

        var initialBalance = await profile.GetAnnualLeaveRemainingAsync();
        Assert.NotNull(initialBalance);

        // ── Step 2: Submit a leave request ───────────────────────────────────
        await profile.ClickRequestLeaveAsync();
        await profile.FillLeaveRequestAsync("Annual Leave", StartDate, EndDate, reason);
        await profile.SubmitLeaveRequestAsync();

        // ── Step 3: Verify the request appears as Pending ─────────────────────
        await _page.WaitForSelectorAsync("table tbody tr", new() { Timeout = 15_000 });
        Assert.Equal("Pending", await profile.GetLeaveRequestStatusAsync(reason));

        // ── Step 4: Cancel the leave request ─────────────────────────────────
        // Find the row and click its "Cancel" action button.
        var row = _page.Locator("table tbody tr")
            .Filter(new() { HasText = reason })
            .First;

        var cancelBtn = row.Locator("button, a")
            .Filter(new() { HasText = "Cancel" })
            .First;
        await cancelBtn.ClickAsync();

        // ── Step 5: Confirm the cancellation — clicking "Cancel" doesn't cancel
        // immediately, it opens a modal confirmation dialog (HrConfirmDialog, titled "Cancel
        // Leave Request") with the message "Are you sure you want to cancel this leave request?"
        // that must be explicitly confirmed via "Yes, Cancel Request" — not the older inline
        // grid "Cancel this request? [Yes] [No]" prompt.
        var confirmDialog = _page.GetByRole(AriaRole.Dialog, new() { Name = "Cancel Leave Request" });
        await confirmDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        Assert.True(await confirmDialog.GetByText("Are you sure you want to cancel this leave request?").IsVisibleAsync(),
            "Expected the confirmation dialog's message");

        await confirmDialog.GetByRole(AriaRole.Button, new() { Name = "Yes, Cancel Request" }).ClickAsync();
        await confirmDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        // ── Step 6: Row stays in the table but status changes to "Cancelled" ──
        // The API keeps cancelled requests visible; only the Cancel button disappears.
        await _page.WaitForFunctionAsync(
            "document.body.innerText.includes('Cancelled')",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

        Assert.Equal("Cancelled", await profile.GetLeaveRequestStatusAsync(reason));

        // The Cancel button must no longer appear for that row.
        var row2 = _page.Locator("table tbody tr")
            .Filter(new() { HasText = reason })
            .First;
        var cancelBtnStillVisible = await row2.Locator("button")
            .Filter(new() { HasText = "Cancel" })
            .IsVisibleAsync();
        Assert.False(cancelBtnStillVisible,
            "Cancel button should not appear once the request is cancelled");

        // ── Step 7: Balance is unchanged — pending cancellation does not deduct
        var finalBalance = await profile.GetAnnualLeaveRemainingAsync();
        Assert.Equal(initialBalance, finalBalance);
    }

    /// <summary>
    /// Dismissing the confirmation dialog via its "No" button must leave the request untouched
    /// (still Pending, with its "Cancel" action still available).
    /// </summary>
    [Fact]
    public async Task DismissingCancelConfirmationDialog_LeavesRequestPending()
    {
        var reason = $"E2E-CANCEL-DISMISS-{Guid.NewGuid():N}";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenLeaveTabAsync();

        await profile.ClickRequestLeaveAsync();
        // Distinct dates from the sibling test above to avoid an overlap validation error.
        await profile.FillLeaveRequestAsync("Annual Leave", "19/01/2026", "21/01/2026", reason);
        await profile.SubmitLeaveRequestAsync();

        await _page.WaitForSelectorAsync("table tbody tr", new() { Timeout = 15_000 });
        Assert.Equal("Pending", await profile.GetLeaveRequestStatusAsync(reason));

        var row = _page.Locator("table tbody tr").Filter(new() { HasText = reason }).First;
        await row.Locator("button, a").Filter(new() { HasText = "Cancel" }).First.ClickAsync();

        var confirmDialog = _page.GetByRole(AriaRole.Dialog, new() { Name = "Cancel Leave Request" });
        await confirmDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await confirmDialog.GetByRole(AriaRole.Button, new() { Name = "No" }).ClickAsync();
        await confirmDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });

        Assert.Equal("Pending", await profile.GetLeaveRequestStatusAsync(reason));
    }
}
