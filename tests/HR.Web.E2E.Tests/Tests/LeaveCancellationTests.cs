using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies that an employee can cancel a pending leave request and that the
/// request is removed from the table — balance is unaffected because the request
/// was never approved.
/// </summary>
[Collection("E2E")]
public sealed class LeaveCancellationTests(AppFixture fixture) : E2ETestBase(fixture)
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
        // immediately, it swaps in an inline "Cancel this request? [Yes] [No]" prompt
        // (MyProfileLeaveTab.razor) that must be explicitly confirmed via "Yes".
        Assert.True(await row.GetByText("Cancel this request?").IsVisibleAsync(),
            "Expected an inline confirmation prompt after clicking 'Cancel'");

        await row.GetByRole(AriaRole.Button, new() { Name = "Yes" }).ClickAsync();

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
}
