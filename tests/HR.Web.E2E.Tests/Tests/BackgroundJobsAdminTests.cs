using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers HR.Admin.Web's Background Jobs page (/jobs — Jobs.razor):
/// - An allow-listed platform admin reaches a usable page: either the three job sections
///   (Scheduled / Running / Failed) or the honest "storage unavailable" dashboard-error state,
///   never a crash or a blank page.
/// - A valid dev-login persona that is not on "PlatformAdmin:AllowedEmails" is rejected
///   server-side and sees the "not authorised / couldn't be loaded" dashboard-error banner, not
///   job data (mirrors CustomerDetailsPageTests / FailedPaymentsDashboardTests).
/// - A genuinely anonymous visitor is redirected to /login by the router before the page runs.
/// - The Retry action opens the AdminActionConfirmDialog ("Retry failed job") which requires a
///   reason of at least 5 characters before it will call BackgroundJobsService.RetryJobAsync, and
///   cancelling closes it without acting.
///
/// The retry confirm/invoke/error paths only run when the shared dev/test environment actually
/// has a failed background job to act on. Hangfire's in-memory/dev storage is seeded with no
/// failed jobs, so the retry-flow tests here are written to no-op (with an explanatory skip-style
/// assertion) when the Failed jobs grid is empty rather than depending on unseeded state — see
/// each test's comment. The confirm-dialog wiring itself (reason validation, cancel) is still
/// asserted whenever a failed job is present.
/// </summary>
public sealed class BackgroundJobsAdminTests(EmployeePersonaFixture fixture) : RoleE2ETestBase<EmployeePersonaFixture>(fixture)
{
    // Seeded platform-admin allow-listed persona — see appsettings.Development.json's
    // "PlatformAdmin:AllowedEmails" and DevPersonaStore.
    private const string AllowListedAdminEmail = "priya.shah@acme.example";

    // Seeded plain-Employee persona (no platform-admin allow-list entry).
    private const string NonAllowListedEmail = "tom.williams@acme.example";

    [Fact]
    public async Task AllowListedAdmin_ReachesUsablePage_NotACrash()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var jobs = new JobsPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await jobs.GoToAsync();

        var sectionsVisible = await jobs.AreJobSectionsVisibleAsync();
        var errorVisible = await jobs.IsErrorBannerVisibleAsync();

        Assert.True(sectionsVisible || errorVisible,
            "Expected either the job sections or the honest storage-unavailable error state for an allow-listed admin");

        if (errorVisible)
        {
            // The only acceptable error for an authorised admin is the storage-availability one,
            // not the "not authorised" branch.
            var text = await jobs.GetErrorBannerTextAsync() ?? "";
            Assert.Contains("unavailable", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task NonAllowListedPersona_IsRejectedAtLogin_NotGivenJobAccess()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        var error = await login.SubmitExpectingNotAuthorisedAsync(NonAllowListedEmail);

        Assert.Contains("not authorised", error, StringComparison.OrdinalIgnoreCase);
        Assert.True(login.IsOnLoginPage(),
            "A non-allow-listed account must be rejected on the login page, not handed a session");
    }

    [Fact]
    public async Task AnonymousAccess_RedirectsToLogin()
    {
        await _page.GotoAsync($"{_fixture.AdminWebBaseUrl}/jobs");
        await _page.WaitForURLAsync(url => url.ToString().Contains("/login"), new() { Timeout = 20_000 });
    }

    [Fact]
    public async Task RetryConfirmDialog_RequiresReason_AndCancelDoesNotAct()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var jobs = new JobsPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await jobs.GoToAsync();

        if (!await jobs.AreJobSectionsVisibleAsync() || await jobs.GetFailedJobRowCountAsync() == 0)
        {
            // No failed job seeded in this environment — the confirm/retry path is not reachable
            // from the UI here. The dialog wiring is still covered by
            // CustomerDetailsPageTests' AdminActionConfirmDialog reason-validation tests (same
            // shared component). Nothing to assert here without a failed job.
            Assert.True(await jobs.IsNoFailedJobsEmptyStateVisibleAsync() || await jobs.IsErrorBannerVisibleAsync());
            return;
        }

        await jobs.OpenRetryDialogForFirstFailedJobAsync();

        // Empty reason -> validation error, dialog stays open, service not called.
        await jobs.ClickRetryConfirmAsync();
        var validation = await jobs.GetRetryValidationErrorAsync() ?? "";
        Assert.Contains("reason", validation, StringComparison.OrdinalIgnoreCase);
        Assert.True(await jobs.IsRetryDialogVisibleAsync(), "Dialog should stay open when no reason is given");

        // Too-short reason (< 5 chars) -> still blocked.
        await jobs.FillRetryReasonAsync("hi");
        await jobs.ClickRetryConfirmAsync();
        Assert.True(await jobs.IsRetryDialogVisibleAsync(), "Dialog should stay open for a too-short reason");

        // Cancel closes without acting — no result message appears.
        await jobs.ClickRetryCancelAsync();
        Assert.False(await jobs.IsRetryDialogVisibleAsync());
        Assert.Null(await jobs.GetActionMessageAsync());
    }

    [Fact]
    public async Task RetryConfirmDialog_WithValidReason_ShowsSingleUsableResult()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var jobs = new JobsPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await jobs.GoToAsync();

        if (!await jobs.AreJobSectionsVisibleAsync() || await jobs.GetFailedJobRowCountAsync() == 0)
        {
            // See RetryConfirmDialog_RequiresReason_AndCancelDoesNotAct — no failed job to retry
            // in this environment.
            return;
        }

        await jobs.OpenRetryDialogForFirstFailedJobAsync();
        await jobs.FillRetryReasonAsync("Re-running after transient dependency outage");
        await jobs.ClickRetryConfirmAsync();

        // Whatever the outcome (requeued, or "may no longer be in a failed state"), the page must
        // land on exactly one result message and close the dialog — never a stale success banner
        // alongside an error, and never a hung disabled dialog.
        await _page.WaitForSelectorAsync(".admin-action-success, .admin-action-error", new() { Timeout = 20_000 });
        Assert.False(await jobs.IsRetryDialogVisibleAsync(), "The retry dialog should close after the action resolves");

        var message = await jobs.GetActionMessageAsync();
        Assert.False(string.IsNullOrWhiteSpace(message), "Expected a single usable result message after retrying");
    }
}
