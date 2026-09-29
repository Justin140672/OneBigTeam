using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class PlatformSettingsManagementTests(ParallelBlankPersonaFixture fixture)
    : RoleE2ETestBase<ParallelBlankPersonaFixture>(fixture)
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

        await settings.GoToAsync();

        var persistedIndex = await settings.FindFlagRowIndexAsync(flagName);
        Assert.True(persistedIndex >= 0, $"Expected feature flag '{flagName}' to persist after reload");
        Assert.True(await settings.IsFlagEnabledAsync(persistedIndex),
            $"Expected feature flag '{flagName}' to be enabled after reload");

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

    [Fact]
    public async Task InvalidTrialLength_Input_IsClampedToMinimum_BeforeSaveEverSubmits()
    {
        var settings = await LoginAndGoToSettingsAsync();

        var originalTrialLength = await settings.GetTrialLengthAsync();
        Assert.False(string.IsNullOrWhiteSpace(originalTrialLength));

        await settings.SetTrialLengthAsync("0");

        Assert.Equal("1", await settings.GetTrialLengthAsync());

        await settings.SaveAsync("E2E: confirming a clamped trial length saves as the valid minimum");
        await _page.WaitForSelectorAsync(".admin-action-success", new() { Timeout = 15_000 });

        await settings.GoToAsync();
        Assert.Equal("1", await settings.GetTrialLengthAsync());

        await settings.SetTrialLengthAsync(originalTrialLength);
        await settings.SaveAsync("E2E: restoring original trial length after clamp test");
        await _page.WaitForSelectorAsync(".admin-action-success", new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task AnonymousAccess_ToSettings_RedirectsToLogin()
    {
        await _page.GotoAsync($"{_fixture.AdminWebBaseUrl}/settings");

        await _page.WaitForURLAsync(url => url.ToString().Contains("/login"), new() { Timeout = 20_000 });
    }
}
