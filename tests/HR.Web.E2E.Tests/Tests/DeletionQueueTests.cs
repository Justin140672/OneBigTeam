using System.Net.Http.Json;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class DeletionQueueTests(CrossUserFixture fixture) : RoleE2ETestBase<CrossUserFixture>(fixture)
{
    private async Task<(Guid CompanyId, string CompanyName)> CreateDisposableCompanyAsync()
    {
        using var http = new HttpClient { BaseAddress = new Uri(_fixture.ApiBaseUrl) };

        var companyName = $"E2E Deletion Co {Guid.NewGuid():N}"[..40];
        var email = $"e2e-deletion-{Guid.NewGuid():N}@example.com";

        var response = await http.PostAsJsonAsync("/api/signup", new
        {
            CompanyName = companyName,
            AdminFirstName = "Ada",
            AdminLastName = "Lovelace",
            AdminEmail = email,
            Password = "P@ssw0rd123",
        });
        response.EnsureSuccessStatusCode();

        var signUp = await response.Content.ReadFromJsonAsync<SignUpResult>();
        Assert.NotNull(signUp);
        return (signUp!.CompanyId, companyName);
    }

    private sealed record SignUpResult(Guid UserId, Guid CompanyId);

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

        var (companyId, companyName) = await CreateDisposableCompanyAsync();

        await details.GoToAsync(companyId);
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

        Assert.True(await queue.HasCompanyAsync(companyName),
            "Expected Beta Corp to appear in the deletion queue after scheduling its deletion");
        Assert.True(await queue.IsPendingAsync(companyName),
            "Expected Beta Corp's deletion queue row to show a Pending status");

        var countdown = await queue.GetCountdownTextAsync(companyName) ?? "";
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

        var (companyId, companyName) = await CreateDisposableCompanyAsync();

        await details.GoToAsync(companyId);

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

        var (companyId, companyName) = await CreateDisposableCompanyAsync();

        await details.GoToAsync(companyId);
        await details.ClickScheduleDeletionAsync();
        await details.FillScheduleDeletionReasonAsync("E2E: ensuring a pending deletion exists to cancel");
        await details.ClickScheduleDeletionConfirmAsync();
        await _page.WaitForSelectorAsync(".admin-action-success", new() { Timeout = 15_000 });

        await queue.GoToAsync();
        Assert.True(await queue.HasCompanyAsync(companyName));

        await queue.ClickCancelDeletionAsync(companyName);
        Assert.True(await queue.IsCancelDeletionDialogVisibleAsync(),
            "Expected the Cancel deletion confirmation dialog to open");

        await queue.FillCancelDeletionReasonAsync("E2E: cancelling Beta Corp's scheduled deletion");
        await queue.ClickCancelDeletionConfirmAsync();

        await _page.WaitForSelectorAsync(".admin-action-success", new() { Timeout = 15_000 });
        Assert.True(await queue.IsCancelledAsync(companyName),
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

        var (companyId, companyName) = await CreateDisposableCompanyAsync();

        await details.GoToAsync(companyId);
        await details.ClickScheduleDeletionAsync();
        await details.FillScheduleDeletionReasonAsync("E2E: ensuring a pending deletion exists for validation check");
        await details.ClickScheduleDeletionConfirmAsync();
        await _page.WaitForSelectorAsync(".admin-action-success", new() { Timeout = 15_000 });

        await queue.GoToAsync();
        Assert.True(await queue.HasCompanyAsync(companyName));

        await queue.ClickCancelDeletionAsync(companyName);
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

        var (companyId, companyName) = await CreateDisposableCompanyAsync();

        await details.GoToAsync(companyId);
        await details.ClickScheduleDeletionAsync();
        await details.FillScheduleDeletionReasonAsync("E2E: ensuring a pending deletion exists to execute");
        await details.ClickScheduleDeletionConfirmAsync();
        await _page.WaitForSelectorAsync(".admin-action-success", new() { Timeout = 15_000 });

        await queue.GoToAsync();
        Assert.True(await queue.HasCompanyAsync(companyName));

        await queue.ClickExecuteNowAsync(companyName);
        Assert.True(await queue.IsExecuteDeletionDialogVisibleAsync(),
            "Expected the Begin controlled deletion confirmation dialog to open");

        // Deliberate wording assertion: the "Execute now" warning must read as a safe, status-only,
        // reversible-in-principle action and must NOT claim it deletes real employee/document/
        // company data — see CustomerDetails.razor's DialogWarning for AdminAction.ScheduleDeletion
        // and DeletionQueue.razor's DialogWarning for DeletionAction.Execute.
        var warningText = await queue.GetExecuteDeletionWarningTextAsync() ?? "";
        Assert.Contains("does not delete customer data", warningText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("permanently deletes all data", warningText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cannot be undone", warningText, StringComparison.OrdinalIgnoreCase);

        await queue.FillExecuteDeletionReasonAsync("E2E: executing Beta Corp's scheduled deletion now");
        await queue.ClickExecuteDeletionConfirmAsync();

        await _page.WaitForSelectorAsync(".admin-action-success", new() { Timeout = 15_000 });
        Assert.True(await queue.IsExecutedAsync(companyName),
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

        var (companyId, companyName) = await CreateDisposableCompanyAsync();

        await details.GoToAsync(companyId);
        await details.ClickScheduleDeletionAsync();
        await details.FillScheduleDeletionReasonAsync("E2E: ensuring a pending deletion exists for validation check");
        await details.ClickScheduleDeletionConfirmAsync();
        await _page.WaitForSelectorAsync(".admin-action-success", new() { Timeout = 15_000 });

        await queue.GoToAsync();
        Assert.True(await queue.HasCompanyAsync(companyName));

        await queue.ClickExecuteNowAsync(companyName);
        await queue.ClickExecuteDeletionConfirmAsync();

        Assert.True(await queue.IsExecuteDeletionDialogVisibleAsync(),
            "Dialog should remain open when no reason is provided");
        var validationText = await queue.GetExecuteDeletionValidationErrorAsync() ?? "";
        Assert.Contains("reason", validationText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnonymousAccess_ToDeletionQueue_RedirectsToLogin()
    {
        await _page.GotoAsync($"{_fixture.AdminWebBaseUrl}/deletion-queue");

        await _page.WaitForURLAsync(url => url.ToString().Contains("/login"), new() { Timeout = 20_000 });
    }
}
