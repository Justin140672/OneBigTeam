using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class PositionProfileManagementTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task PositionProfileList_ShowsSeededProfiles()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var ppList   = new PositionProfileListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await ppList.GoToAsync(AcmeId);

        Assert.True(await ppList.HasPositionProfileAsync("Chief Technology Officer"),
            "Expected 'Chief Technology Officer' in the position profile list");
        Assert.True(await ppList.HasPositionProfileAsync("HR Manager"),
            "Expected 'HR Manager' in the position profile list");
        Assert.True(await ppList.HasPositionProfileAsync("Software Engineer"),
            "Expected 'Software Engineer' in the position profile list");
    }

    [Fact]
    public async Task CreatePositionProfile_AppearsInList()
    {
        var profileTitle = $"E2E Role {Guid.NewGuid().ToString("N")[..8]}";

        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var ppList   = new PositionProfileListPage(_page, _fixture.WebBaseUrl);
        var ppEdit   = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await ppList.GoToAsync(AcmeId);
        await ppList.ClickNewPositionProfileAsync();

        await ppEdit.FillTitleAsync(profileTitle);
        await ppEdit.FillDescriptionAsync("Created by E2E test");
        await ppEdit.SelectDepartmentAsync("Engineering");
        await ppEdit.SelectLocationAsync("London Office");
        await ppEdit.SelectDefaultLeavePolicyAsync("Standard");
        await ppEdit.SaveAsync();

        Assert.True(await ppList.HasPositionProfileAsync(profileTitle),
            $"Expected the new position profile '{profileTitle}' to appear in the list");
    }

    [Fact]
    public async Task CreatePositionProfile_PersistsTemplateDefaults()
    {
        var profileTitle = $"E2E Template {Guid.NewGuid().ToString("N")[..8]}";

        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var ppList = new PositionProfileListPage(_page, _fixture.WebBaseUrl);
        var ppEdit = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await ppList.GoToAsync(AcmeId);
        await ppList.ClickNewPositionProfileAsync();

        await ppEdit.FillTitleAsync(profileTitle);
        await ppEdit.FillProbationMonthsOverrideAsync(3);
        await ppEdit.FillSalaryRangeAsync(40000, 60000);
        await ppEdit.SelectDepartmentAsync("Engineering");
        await ppEdit.SelectLocationAsync("London Office");
        await ppEdit.SelectDefaultLeavePolicyAsync("Standard");
        await ppEdit.SetUseCompanyWorkingPatternAsync(false);
        await ppEdit.SaveAsync();

        Assert.True(await ppList.HasPositionProfileAsync(profileTitle),
            $"Expected the new position profile '{profileTitle}' to appear in the list");

        await ppList.OpenPositionProfileAsync(profileTitle);

        Assert.Equal("3", await _page.GetByPlaceholder("Use company default").InputValueAsync());
        Assert.Equal("40000", await _page.GetByPlaceholder("Min").InputValueAsync());
        Assert.Equal("60000", await _page.GetByPlaceholder("Max").InputValueAsync());

        var useCompanyWorkingPattern = await _page.GetByLabel("Use company working pattern").IsCheckedAsync();
        Assert.False(useCompanyWorkingPattern, "Expected the working pattern override to persist across reload");
    }

    [Fact]
    public async Task CreatePositionProfile_WithEmptyTitle_ShowsValidationError()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var ppEdit  = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await ppEdit.GoToNewAsync(AcmeId);

        await _page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await _page.WaitForFunctionAsync(
            "document.querySelector('.alert-danger, .validation-message') !== null " +
            "|| !window.location.href.includes('/position-profiles/new')",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

        Assert.Contains("/position-profiles/new", _page.Url);
        Assert.True(await ppEdit.HasErrorAsync(),
            "Expected a validation error when saving a position profile with no title");
    }

    [Fact]
    public async Task PositionProfile_SetAndClearOnboardingTemplate_PersistsAcrossReload()
    {
        var profileTitle = $"E2E Onboarding Link {Guid.NewGuid().ToString("N")[..8]}";
        var templateName = $"E2E Template {Guid.NewGuid().ToString("N")[..8]}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var ppList        = new PositionProfileListPage(_page, _fixture.WebBaseUrl);
        var ppEdit        = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);
        var templateEdit  = new OnboardingTemplateEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await new OnboardingTemplateListPage(_page, _fixture.WebBaseUrl).GoToAsync(AcmeId);

        await templateEdit.GoToNewAsync(AcmeId);
        await templateEdit.FillNameAsync(templateName);
        await templateEdit.SaveAsync();

        await ppList.GoToAsync(AcmeId);
        await ppList.ClickNewPositionProfileAsync();

        await ppEdit.FillTitleAsync(profileTitle);
        await ppEdit.SelectDepartmentAsync("Engineering");
        await ppEdit.SelectLocationAsync("London Office");
        await ppEdit.SelectDefaultLeavePolicyAsync("Standard");
        await ppEdit.SelectOnboardingTemplateAsync(templateName);
        await ppEdit.SaveAsync();

        await ppList.OpenPositionProfileAsync(profileTitle);
        Assert.Equal(templateName, await ppEdit.GetSelectedOnboardingTemplateTextAsync());

        await ppEdit.ClearOnboardingTemplateAsync();
        await ppEdit.SaveAsync();

        await ppList.OpenPositionProfileAsync(profileTitle);
        Assert.Equal("None", await ppEdit.GetSelectedOnboardingTemplateTextAsync());
    }

    [Fact]
    public async Task DeactivatePositionProfile_HidesFromActiveList_ShowsWhenInactiveToggled()
    {
        var profileTitle = $"E2E Deact {Guid.NewGuid().ToString("N")[..8]}";

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
        await ppEdit.SaveAsync();

        await ppList.GoToAsync(AcmeId);
        Assert.True(await ppList.IsActiveAsync(profileTitle), "Expected newly created position profile to be Active");
        await ppList.DeactivateAsync(profileTitle);

        Assert.False(await ppList.HasPositionProfileAsync(profileTitle),
            $"Expected '{profileTitle}' to no longer appear in the default active-only view after deactivation");

        await ppList.ShowInactiveAsync();

        Assert.True(await ppList.HasPositionProfileAsync(profileTitle),
            "Expected deactivated position profile to appear when 'Show Inactive' is enabled");
        Assert.False(await ppList.IsActiveAsync(profileTitle),
            "Expected deactivated position profile to show an inactive indicator");
    }

    [Fact]
    public async Task PlainEmployee_IsRedirectedAway_FromPositionProfilesPage()
    {
        const string tomEmail = "tom.williams@acme.example";

        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(tomEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/position-profiles");

        await WaitForUrlToStopContainingAsync("/position-profiles");

        var finalUrl = _page.Url;
        Assert.False(finalUrl.Contains("/position-profiles"),
            $"Expected a plain employee to be redirected away from the position profiles page, but ended up at: {finalUrl}");
    }
}
