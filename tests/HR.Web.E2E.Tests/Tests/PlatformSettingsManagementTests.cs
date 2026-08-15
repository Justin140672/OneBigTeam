using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies HR.Admin.Web's Platform Settings page (/settings), the final Admin Portal backlog
/// item — a single global-config form (trial length, default monthly price, support email,
/// maintenance mode + message, feature flags) saved through the shared AdminActionConfirmDialog
/// (mandatory reason, min 5 chars — see AdminActionConfirmDialog.razor). Save (Settings.razor's
/// OnConfirmedAsync) re-fetches the settings from the server on success and re-populates the form
/// in-place, so assertions after a successful save read the same in-page fields rather than
/// requiring a manual reload; the feature-flag persistence test still does an explicit reload to
/// prove the value survived a fresh GET, not just the optimistic in-memory state.
///
/// Uses "priya.shah@acme.example" as the allow-listed admin — same convention as
/// AdminUsersManagementTests/DeletionQueueTests/CustomerDetailsPageTests. That email is both in
/// the "PlatformAdmin:AllowedEmails" config allow-list AND bootstrap-seeded as an enabled
/// PlatformOwner row, so it should already be authorised to view/manage this page without any
/// additional E2E fixture changes.
///
/// Each test that saves settings uses a GUID-suffixed feature flag name (where relevant) so
/// repeated runs against the same fixture don't collide, and each test restores the trial length
/// to a valid value by the time it finishes changing shared global state, since Platform Settings
/// is a true singleton shared across the whole fixture (unlike per-row admin-user tests).
/// </summary>
[Collection("E2E")]
public sealed class PlatformSettingsManagementTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private const string AllowListedAdminEmail = "priya.shah@acme.example";

    private static string NewFlagName() => $"e2e-flag-{Guid.NewGuid():N}";

    private async Task<SettingsPage> LoginAndGoToSettingsAsync()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var settings = new SettingsPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await settings.GoToAsync();
        return settings;
    }

    [Fact]
    public async Task Settings_AllowListedAdmin_LoadsFormPopulatedWithCurrentValues()
    {
        var settings = await LoginAndGoToSettingsAsync();

        Assert.False(await settings.IsErrorBannerVisibleAsync(),
            "Expected the allow-listed admin to see the settings form, not the error banner");
        Assert.True(await settings.IsFormVisibleAsync(), "Expected the settings form to render");

        // Bootstrap-seeded platform settings should have some non-empty support email and a
        // positive trial length — assert the fields aren't blank rather than pinning exact
        // seeded values, which this test doesn't own.
        Assert.False(string.IsNullOrWhiteSpace(await settings.GetTrialLengthAsync()));
        Assert.False(string.IsNullOrWhiteSpace(await settings.GetSupportEmailAsync()));

        var lastUpdatedWhen = await settings.GetLastUpdatedWhenTextAsync();
        Assert.False(string.IsNullOrWhiteSpace(lastUpdatedWhen),
            "Expected a non-empty 'Last updated / When' read-only value");
    }

    [Fact]
    public async Task EditTrialLengthAndSupportEmail_Save_ShowsSuccessAndPersistsValues()
    {
        var settings = await LoginAndGoToSettingsAsync();

        var newTrialLength = "21";
        var newSupportEmail = $"support-{Guid.NewGuid():N}@example.test";

        await settings.SetTrialLengthAsync(newTrialLength);
        await settings.SetSupportEmailAsync(newSupportEmail);

        await settings.SaveAsync("E2E: updating trial length and support email");

        await _page.WaitForSelectorAsync(".admin-action-success", new() { Timeout = 15_000 });
        Assert.True(await settings.IsSuccessBannerVisibleAsync(),
            "Expected a success banner after saving valid platform settings");

        Assert.Equal(newTrialLength, await settings.GetTrialLengthAsync());
        Assert.Equal(newSupportEmail, await settings.GetSupportEmailAsync());
    }

    [Fact]
    public async Task ToggleMaintenanceModeOn_RevealsMessageField_SaveAndPersist()
    {
        var settings = await LoginAndGoToSettingsAsync();

        // Start from a known "off" state so this test is not order-dependent on a prior test
        // having left maintenance mode enabled.
        if (await settings.IsMaintenanceModeCheckedAsync())
        {
            await settings.ToggleMaintenanceModeAsync();
            await settings.SaveAsync("E2E: resetting maintenance mode to off before test");
            await _page.WaitForSelectorAsync(".admin-action-success", new() { Timeout = 15_000 });
        }

        Assert.False(await settings.IsMaintenanceMessageVisibleAsync(),
            "Expected the maintenance message field to be hidden while maintenance mode is off");

        await settings.ToggleMaintenanceModeAsync();
        Assert.True(await settings.IsMaintenanceModeCheckedAsync());
        Assert.True(await settings.IsMaintenanceMessageVisibleAsync(),
            "Expected the maintenance message field to appear once maintenance mode is checked");

        var message = "E2E maintenance message: scheduled maintenance in progress.";
        await settings.SetMaintenanceMessageAsync(message);

        await settings.SaveAsync("E2E: enabling maintenance mode with a message");

        await _page.WaitForSelectorAsync(".admin-action-success", new() { Timeout = 15_000 });
        Assert.True(await settings.IsSuccessBannerVisibleAsync());
        Assert.True(await settings.IsMaintenanceModeCheckedAsync());
        Assert.True(await settings.IsMaintenanceMessageVisibleAsync());
        Assert.Equal(message, await settings.GetMaintenanceMessageAsync());

        // Clean up: turn maintenance mode back off so it doesn't affect other tests/other admins
        // interacting with the platform during the same fixture run.
        await settings.ToggleMaintenanceModeAsync();
        await settings.SaveAsync("E2E: disabling maintenance mode after test");
        await _page.WaitForSelectorAsync(".admin-action-success", new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task AddFeatureFlag_Save_PersistsAfterReload_ThenRemoveFlag_Save_RemovesAfterReload()
    {
        var settings = await LoginAndGoToSettingsAsync();

        var flagName = NewFlagName();
        var rowCountBefore = await settings.GetFeatureFlagRowCountAsync();

        await settings.ClickAddFlagAsync();
        Assert.Equal(rowCountBefore + 1, await settings.GetFeatureFlagRowCountAsync());

        var newRowIndex = rowCountBefore;
        await settings.SetFlagNameAsync(newRowIndex, flagName);
        await settings.ToggleFlagEnabledAsync(newRowIndex);
        Assert.True(await settings.IsFlagEnabledAsync(newRowIndex));

        await settings.SaveAsync("E2E: adding a new feature flag");
        await _page.WaitForSelectorAsync(".admin-action-success", new() { Timeout = 15_000 });
        Assert.True(await settings.IsSuccessBannerVisibleAsync());

        // Reload from a fresh navigation (a fresh GET) to prove the flag was actually persisted
        // server-side, not just left over in the page's in-memory state after the save's
        // in-place LoadAsync() re-population.
        await settings.GoToAsync();

        var persistedIndex = await settings.FindFlagRowIndexAsync(flagName);
        Assert.True(persistedIndex >= 0, $"Expected feature flag '{flagName}' to persist after reload");
        Assert.True(await settings.IsFlagEnabledAsync(persistedIndex),
            $"Expected feature flag '{flagName}' to be enabled after reload");

        // Now remove it (before saving) and confirm removing a row before save works, then save
        // and confirm it's gone after a reload too, so this test cleans up after itself.
        await settings.RemoveFlagAsync(persistedIndex);
        Assert.Equal(-1, await settings.FindFlagRowIndexAsync(flagName));

        await settings.SaveAsync("E2E: removing the feature flag added by this test");
        await _page.WaitForSelectorAsync(".admin-action-success", new() { Timeout = 15_000 });

        await settings.GoToAsync();
        Assert.Equal(-1, await settings.FindFlagRowIndexAsync(flagName));
    }

    [Fact]
    public async Task RemoveFeatureFlagRow_BeforeSave_RemovesRowImmediately()
    {
        var settings = await LoginAndGoToSettingsAsync();

        var rowCountBefore = await settings.GetFeatureFlagRowCountAsync();
        await settings.ClickAddFlagAsync();
        Assert.Equal(rowCountBefore + 1, await settings.GetFeatureFlagRowCountAsync());

        await settings.RemoveFlagAsync(rowCountBefore);
        Assert.Equal(rowCountBefore, await settings.GetFeatureFlagRowCountAsync());
    }

    /// <summary>
    /// The trial-length SfNumericTextBox is configured with Min="1" (Settings.razor), and
    /// Syncfusion clamps an out-of-range typed value back to the bound (here, up to 1) on blur —
    /// before Save is ever clicked, so "0" can never actually reach the server as an invalid
    /// payload via the UI. This test verifies that real, observable behavior (the field
    /// self-corrects and a save with the corrected value succeeds) rather than an unreachable
    /// server-round-trip validation error — server-side rejection of an invalid trial length is
    /// covered separately at the unit level (UpdatePlatformSettingsValidatorTests).
    /// </summary>
    [Fact]
    public async Task InvalidTrialLength_Input_IsClampedToMinimum_BeforeSaveEverSubmits()
    {
        var settings = await LoginAndGoToSettingsAsync();

        var originalTrialLength = await settings.GetTrialLengthAsync();
        Assert.False(string.IsNullOrWhiteSpace(originalTrialLength));

        await settings.SetTrialLengthAsync("0");

        // Syncfusion's Min clamp fires on blur (the Tab press inside SetTrialLengthAsync), so the
        // field's own value is already corrected before Save is clicked at all.
        Assert.Equal("1", await settings.GetTrialLengthAsync());

        await settings.SaveAsync("E2E: confirming a clamped trial length saves as the valid minimum");
        await _page.WaitForSelectorAsync(".admin-action-success", new() { Timeout = 15_000 });

        // Reload to confirm the clamped value (1), not 0, is what was actually persisted.
        await settings.GoToAsync();
        Assert.Equal("1", await settings.GetTrialLengthAsync());

        // Restore the original value so this test doesn't leave shared global state at 1 for
        // whichever test runs next against the same singleton settings row.
        await settings.SetTrialLengthAsync(originalTrialLength);
        await settings.SaveAsync("E2E: restoring original trial length after clamp test");
        await _page.WaitForSelectorAsync(".admin-action-success", new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task AnonymousAccess_ToSettings_RedirectsToLogin()
    {
        // Same pattern as AdminUsersManagementTests.AnonymousAccess_ToAdminUsers_RedirectsToLogin
        // and DeletionQueueTests.AnonymousAccess_ToDeletionQueue_RedirectsToLogin: navigate
        // directly rather than via SettingsPage.GoToAsync, which waits for that page's own
        // settled-state selectors and would time out on /login.
        await _page.GotoAsync($"{_fixture.AdminWebBaseUrl}/settings");

        await _page.WaitForURLAsync(url => url.ToString().Contains("/login"), new() { Timeout = 20_000 });
    }
}
