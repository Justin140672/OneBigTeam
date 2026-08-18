using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies HR.Admin.Web's Permanent Deletion Queue (/deletion-queue) and the "Schedule deletion"
/// entry point on Customer Details' Subscription management panel (Customer Lifecycle epic):
/// - An allow-listed platform admin can load the queue page (even with zero eligible rows).
/// - Scheduling a deletion from Customer Details makes the company appear in the queue as
///   Pending, with a live countdown.
/// - Cancelling a pending deletion from the queue (reason required) flips it to Cancelled.
/// - Executing a deletion now (reason required) flips it to Executed, and the confirmation
///   dialog's warning copy explicitly disclaims real data destruction — a deliberate wording
///   choice (see DeletionQueue.razor / CustomerDetails.razor DialogWarning), asserted on here so a
///   future edit can't silently reintroduce alarming "this deletes everything" language.
/// - Anonymous access to /deletion-queue is blocked at the router level, same pattern as
///   CustomerDetailsPageTests.AnonymousAccess_RedirectsToLogin.
///
/// Uses the second seeded dev/E2E company (Beta Corp, id ...002) rather than Acme for the
/// schedule/cancel/execute flows, since those are destructive to subscription/access state and
/// Acme is relied on as a stable "fully active" fixture by many other test classes. Beta Corp is
/// itself reused elsewhere (see TenantIsolationTests etc.) as a second-tenant fixture, so these
/// tests run its deletion lifecycle end-to-end (schedule -> cancel, then separately schedule ->
/// execute) rather than leaving it in a half-mutated state, and should be treated as a soft
/// candidate for its own dedicated third seeded company if Beta Corp's mutated state ever
/// conflicts with other suites — see remarks in the class-level test report.
/// </summary>
public sealed class DeletionQueueTests(CrossUserFixture fixture) : CrossUserTenantAndMiscTestBase(fixture)
{
    private static readonly Guid BetaCorpId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private const string AllowListedAdminEmail = "priya.shah@acme.example";

    [Fact]
    public async Task DeletionQueue_AllowListedAdmin_LoadsList()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var queue = new DeletionQueuePage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await queue.GoToAsync();

        Assert.False(await queue.IsErrorBannerVisibleAsync(),
            "Expected the allow-listed admin to see the deletion queue, not the error banner");

        // The queue may legitimately be empty (no deletions ever scheduled) or already have rows
        // from a previous test run against the same fixture — either the empty-state message or
        // the table is an acceptable "list rendered" outcome.
        var isEmpty = await queue.IsEmptyStateVisibleAsync();
        var hasTable = await queue.IsTableVisibleAsync();
        Assert.True(isEmpty || hasTable,
            "Expected either the empty-state message or the deletion queue table to render");
    }

    [Fact]
    public async Task ScheduleDeletion_FromCustomerDetails_AppearsInQueueAsPendingWithCountdown()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var details = new CustomerDetailsPage(_page, _fixture.AdminWebBaseUrl);
        var queue = new DeletionQueuePage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await details.GoToAsync(BetaCorpId);
        Assert.False(await details.IsErrorBannerVisibleAsync(),
            "Expected the allow-listed admin to see Beta Corp's details, not the error banner");

        await details.ClickScheduleDeletionAsync();
        Assert.True(await details.IsScheduleDeletionDialogVisibleAsync(),
            "Expected the Schedule deletion confirmation dialog to open");

        await details.FillScheduleDeletionReasonAsync("E2E: scheduling deletion for queue coverage");
        await details.ClickScheduleDeletionConfirmAsync();

        await _page.WaitForSelectorAsync(".admin-action-success", new() { Timeout = 15_000 });
        Assert.True(await details.IsSubscriptionActionSuccessVisibleAsync(),
            "Expected a success message in the Subscription management panel after scheduling deletion");

        await queue.GoToAsync();

        Assert.True(await queue.HasCompanyAsync("Beta Corp"),
            "Expected Beta Corp to appear in the deletion queue after scheduling its deletion");
        Assert.True(await queue.IsPendingAsync("Beta Corp"),
            "Expected Beta Corp's deletion queue row to show a Pending status");

        var countdown = await queue.GetCountdownTextAsync("Beta Corp") ?? "";
        Assert.False(string.IsNullOrWhiteSpace(countdown));
        Assert.DoesNotContain("Overdue", countdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScheduleDeletionWithoutReason_IsBlockedWithValidationError()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var details = new CustomerDetailsPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await details.GoToAsync(BetaCorpId);

        await details.ClickScheduleDeletionAsync();
        await details.ClickScheduleDeletionConfirmAsync();

        Assert.True(await details.IsScheduleDeletionDialogVisibleAsync(),
            "Dialog should remain open when no reason is provided");
        var validationText = await details.GetScheduleDeletionValidationErrorAsync() ?? "";
        Assert.Contains("reason", validationText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelPendingDeletion_FromQueue_UpdatesStatusToCancelled()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var details = new CustomerDetailsPage(_page, _fixture.AdminWebBaseUrl);
        var queue = new DeletionQueuePage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        // Ensure there's a pending deletion for Beta Corp to cancel — scheduling again while one is
        // already pending is a no-op-equivalent action from the UI's perspective (still lands on the
        // same "Pending" row), so this is safe to run independently of the scheduling test above.
        await details.GoToAsync(BetaCorpId);
        await details.ClickScheduleDeletionAsync();
        await details.FillScheduleDeletionReasonAsync("E2E: ensuring a pending deletion exists to cancel");
        await details.ClickScheduleDeletionConfirmAsync();
        await _page.WaitForSelectorAsync(".admin-action-success", new() { Timeout = 15_000 });

        await queue.GoToAsync();
        Assert.True(await queue.HasCompanyAsync("Beta Corp"));

        await queue.ClickCancelDeletionAsync("Beta Corp");
        Assert.True(await queue.IsCancelDeletionDialogVisibleAsync(),
            "Expected the Cancel deletion confirmation dialog to open");

        await queue.FillCancelDeletionReasonAsync("E2E: cancelling Beta Corp's scheduled deletion");
        await queue.ClickCancelDeletionConfirmAsync();

        await _page.WaitForSelectorAsync(".admin-action-success", new() { Timeout = 15_000 });
        Assert.True(await queue.IsCancelledAsync("Beta Corp"),
            "Expected Beta Corp's deletion queue row to show a Cancelled status after cancelling");
    }

    [Fact]
    public async Task CancelDeletionWithoutReason_IsBlockedWithValidationError()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var details = new CustomerDetailsPage(_page, _fixture.AdminWebBaseUrl);
        var queue = new DeletionQueuePage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        // Ensure a pending row exists so the "Cancel deletion" action is available on the row.
        await details.GoToAsync(BetaCorpId);
        await details.ClickScheduleDeletionAsync();
        await details.FillScheduleDeletionReasonAsync("E2E: ensuring a pending deletion exists for validation check");
        await details.ClickScheduleDeletionConfirmAsync();
        await _page.WaitForSelectorAsync(".admin-action-success", new() { Timeout = 15_000 });

        await queue.GoToAsync();
        Assert.True(await queue.HasCompanyAsync("Beta Corp"));

        await queue.ClickCancelDeletionAsync("Beta Corp");
        await queue.ClickCancelDeletionConfirmAsync();

        Assert.True(await queue.IsCancelDeletionDialogVisibleAsync(),
            "Dialog should remain open when no reason is provided");
        var validationText = await queue.GetCancelDeletionValidationErrorAsync() ?? "";
        Assert.Contains("reason", validationText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteDeletionNow_FromQueue_UpdatesStatusToExecuted_AndWarningDoesNotImplyRealDataDestruction()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var details = new CustomerDetailsPage(_page, _fixture.AdminWebBaseUrl);
        var queue = new DeletionQueuePage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        // Ensure a pending deletion exists (schedule again if a previous test already cancelled or
        // executed it — scheduling while one is already pending is safe/idempotent from the UI's
        // perspective, see remarks above).
        await details.GoToAsync(BetaCorpId);
        await details.ClickScheduleDeletionAsync();
        await details.FillScheduleDeletionReasonAsync("E2E: ensuring a pending deletion exists to execute");
        await details.ClickScheduleDeletionConfirmAsync();
        await _page.WaitForSelectorAsync(".admin-action-success", new() { Timeout = 15_000 });

        await queue.GoToAsync();
        Assert.True(await queue.HasCompanyAsync("Beta Corp"));

        await queue.ClickExecuteNowAsync("Beta Corp");
        Assert.True(await queue.IsExecuteDeletionDialogVisibleAsync(),
            "Expected the Execute deletion now confirmation dialog to open");

        // Deliberate wording assertion: the "Execute now" warning must read as a safe, status-only,
        // reversible-in-principle action and must NOT claim it deletes real employee/document/
        // company data — see CustomerDetails.razor's DialogWarning for AdminAction.ScheduleDeletion
        // and DeletionQueue.razor's DialogWarning for DeletionAction.Execute.
        var warningText = await queue.GetExecuteDeletionWarningTextAsync() ?? "";
        Assert.Contains("does NOT delete any real", warningText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("permanently deletes all data", warningText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cannot be undone", warningText, StringComparison.OrdinalIgnoreCase);

        await queue.FillExecuteDeletionReasonAsync("E2E: executing Beta Corp's scheduled deletion now");
        await queue.ClickExecuteDeletionConfirmAsync();

        await _page.WaitForSelectorAsync(".admin-action-success", new() { Timeout = 15_000 });
        Assert.True(await queue.IsExecutedAsync("Beta Corp"),
            "Expected Beta Corp's deletion queue row to show an Executed status after executing now");
    }

    [Fact]
    public async Task ExecuteDeletionWithoutReason_IsBlockedWithValidationError()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var details = new CustomerDetailsPage(_page, _fixture.AdminWebBaseUrl);
        var queue = new DeletionQueuePage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        // Ensure a pending row exists so the "Execute now" action is available on the row.
        await details.GoToAsync(BetaCorpId);
        await details.ClickScheduleDeletionAsync();
        await details.FillScheduleDeletionReasonAsync("E2E: ensuring a pending deletion exists for validation check");
        await details.ClickScheduleDeletionConfirmAsync();
        await _page.WaitForSelectorAsync(".admin-action-success", new() { Timeout = 15_000 });

        await queue.GoToAsync();
        Assert.True(await queue.HasCompanyAsync("Beta Corp"));

        await queue.ClickExecuteNowAsync("Beta Corp");
        await queue.ClickExecuteDeletionConfirmAsync();

        Assert.True(await queue.IsExecuteDeletionDialogVisibleAsync(),
            "Dialog should remain open when no reason is provided");
        var validationText = await queue.GetExecuteDeletionValidationErrorAsync() ?? "";
        Assert.Contains("reason", validationText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnonymousAccess_ToDeletionQueue_RedirectsToLogin()
    {
        // Same pattern as CustomerDetailsPageTests.AnonymousAccess_RedirectsToLogin: navigate
        // directly rather than via DeletionQueuePage.GoToAsync, which waits for that page's own
        // settled-state selectors and would time out on /login.
        await _page.GotoAsync($"{_fixture.AdminWebBaseUrl}/deletion-queue");

        await _page.WaitForURLAsync(url => url.ToString().Contains("/login"), new() { Timeout = 20_000 });
    }
}
