using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class PositionProfileNoticePeriodOverrideTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task CreatePositionProfile_WithoutNoticePeriodOverride_FieldsStayHiddenAndSavesFine()
    {
        var profileTitle = $"E2E No Notice Override {Guid.NewGuid().ToString("N")[..8]}";

        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var ppList = new PositionProfileListPage(_page, _fixture.WebBaseUrl);
        var ppEdit = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await ppList.GoToAsync(AcmeId);
        await ppList.ClickNewPositionProfileAsync();

        await ppEdit.FillTitleAsync(profileTitle);
        await ppEdit.SelectDepartmentAsync("Engineering");
        await ppEdit.SelectLocationAsync("London Office");
        await ppEdit.SelectDefaultLeavePolicyAsync("Standard");

        Assert.False(await ppEdit.IsOverrideNoticePeriodCheckedAsync(),
            "Expected the notice period override checkbox to be unchecked by default");
        Assert.False(await ppEdit.IsNoticePeriodOverrideFieldsVisibleAsync(),
            "Expected the Unit/Length fields to stay hidden while the override is unchecked");

        await ppEdit.SaveAsync();

        Assert.True(await ppList.HasPositionProfileAsync(profileTitle),
            $"Expected the new position profile '{profileTitle}' to appear in the list");

        await ppList.OpenPositionProfileAsync(profileTitle);
        Assert.False(await ppEdit.IsOverrideNoticePeriodCheckedAsync(),
            "Expected the notice period override to remain unchecked after reload");
        Assert.False(await ppEdit.IsNoticePeriodOverrideFieldsVisibleAsync(),
            "Expected the Unit/Length fields to remain hidden after reload");
    }

    [Fact]
    public async Task CreatePositionProfile_WithNoticePeriodOverride_PersistsUnitAndLengthAcrossReload()
    {
        var profileTitle = $"E2E Notice Override {Guid.NewGuid().ToString("N")[..8]}";

        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var ppList = new PositionProfileListPage(_page, _fixture.WebBaseUrl);
        var ppEdit = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await ppList.GoToAsync(AcmeId);
        await ppList.ClickNewPositionProfileAsync();

        await ppEdit.FillTitleAsync(profileTitle);
        await ppEdit.SelectDepartmentAsync("Engineering");
        await ppEdit.SelectLocationAsync("London Office");
        await ppEdit.SelectDefaultLeavePolicyAsync("Standard");

        await ppEdit.SetOverrideNoticePeriodAsync(true);
        await ppEdit.SelectNoticePeriodUnitOverrideAsync("Weeks");
        await ppEdit.FillNoticePeriodLengthOverrideAsync(4);

        await ppEdit.SaveAsync();

        Assert.True(await ppList.HasPositionProfileAsync(profileTitle),
            $"Expected the new position profile '{profileTitle}' to appear in the list");

        await ppList.OpenPositionProfileAsync(profileTitle);

        Assert.True(await ppEdit.IsOverrideNoticePeriodCheckedAsync(),
            "Expected the notice period override checkbox to be checked after reload");
        Assert.True(await ppEdit.IsNoticePeriodOverrideFieldsVisibleAsync(),
            "Expected the Unit/Length fields to be visible after reload");
        Assert.Equal("Weeks", await ppEdit.GetNoticePeriodUnitOverrideTextAsync());
        Assert.Equal(4, await ppEdit.GetNoticePeriodLengthOverrideAsync());
    }

    [Fact]
    public async Task EditPositionProfile_TurnOffNoticePeriodOverride_ClearsAndHidesFieldsAcrossReload()
    {
        var profileTitle = $"E2E Notice Override Off {Guid.NewGuid().ToString("N")[..8]}";

        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var ppList = new PositionProfileListPage(_page, _fixture.WebBaseUrl);
        var ppEdit = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await ppList.GoToAsync(AcmeId);
        await ppList.ClickNewPositionProfileAsync();

        await ppEdit.FillTitleAsync(profileTitle);
        await ppEdit.SelectDepartmentAsync("Engineering");
        await ppEdit.SelectLocationAsync("London Office");
        await ppEdit.SelectDefaultLeavePolicyAsync("Standard");

        await ppEdit.SetOverrideNoticePeriodAsync(true);
        await ppEdit.SelectNoticePeriodUnitOverrideAsync("Months");
        await ppEdit.FillNoticePeriodLengthOverrideAsync(2);

        await ppEdit.SaveAsync();

        await ppList.OpenPositionProfileAsync(profileTitle);
        Assert.True(await ppEdit.IsOverrideNoticePeriodCheckedAsync(),
            "Expected the notice period override checkbox to be checked before editing it off");

        await ppEdit.SetOverrideNoticePeriodAsync(false);
        await ppEdit.SaveAsync();

        await ppList.OpenPositionProfileAsync(profileTitle);
        Assert.False(await ppEdit.IsOverrideNoticePeriodCheckedAsync(),
            "Expected the notice period override checkbox to be unchecked after saving it off");
        Assert.False(await ppEdit.IsNoticePeriodOverrideFieldsVisibleAsync(),
            "Expected the Unit/Length fields to be hidden after the override was turned off and reloaded");
    }
}
