using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class OnboardingTemplateManagementTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";
    private const string SeededTemplateName = "Standard Onboarding";

    [Fact]
    public async Task EditOnboardingTemplate_PersistsAcrossReload()
    {
        var originalName = $"E2E Onboarding {Guid.NewGuid().ToString("N")[..8]}";
        var updatedName = $"{originalName} Updated";
        var originalTaskTitle = $"E2E Task {Guid.NewGuid().ToString("N")[..8]}";
        var updatedTaskTitle = $"{originalTaskTitle} Updated";

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var templateEdit = new OnboardingTemplateEditPage(_page, _fixture.WebBaseUrl);
        var templateList = new OnboardingTemplateListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await templateList.GoToAsync(AcmeId);

        await templateEdit.GoToNewAsync(AcmeId);
        await templateEdit.FillNameAsync(originalName);
        await templateEdit.FillDescriptionAsync("Created by E2E test");
        await templateEdit.ClickAddTaskAsync();
        await templateEdit.FillTaskTitleAsync(originalTaskTitle);
        await templateEdit.SaveAsync();

        await _page.WaitForSelectorAsync(".e-grid .e-row, .e-grid .e-emptyrow", new() { Timeout = 15_000 });
        await _page.RevealGridRowAsync(originalName);
        var href = await _page.Locator(".e-rowcell a").Filter(new() { HasText = originalName }).First.GetAttributeAsync("href");
        Assert.NotNull(href);
        await _page.GotoAsync($"{_fixture.WebBaseUrl}{href}");
        await _page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });

        await templateEdit.FillNameAsync(updatedName);
        await templateEdit.FillTaskTitleAsync(updatedTaskTitle);
        await templateEdit.SaveAsync();

        await _page.WaitForSelectorAsync(".e-grid .e-row, .e-grid .e-emptyrow", new() { Timeout = 15_000 });
        await _page.RevealGridRowAsync(updatedName);
        var updatedHref = await _page.Locator(".e-rowcell a").Filter(new() { HasText = updatedName }).First.GetAttributeAsync("href");
        Assert.NotNull(updatedHref);
        await _page.GotoAsync($"{_fixture.WebBaseUrl}{updatedHref}");
        await _page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });

        await _page.ReloadAsync();
        await _page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });

        Assert.Equal(updatedName, await templateEdit.GetNameAsync());
        Assert.Equal(updatedTaskTitle, await templateEdit.GetTaskTitleAsync());
    }

    [Fact]
    public async Task DeactivateOnboardingTemplate_HiddenFromActiveList_VisibleWhenShowingInactive()
    {
        var templateName = $"E2E Onboarding Deact {Guid.NewGuid().ToString("N")[..8]}";

        var login        = new LoginPage(_page, _fixture.WebBaseUrl);
        var templateList = new OnboardingTemplateListPage(_page, _fixture.WebBaseUrl);
        var templateEdit = new OnboardingTemplateEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await templateList.GoToAsync(AcmeId);

        await templateEdit.GoToNewAsync(AcmeId);
        await templateEdit.FillNameAsync(templateName);
        await templateEdit.SaveAsync();

        await templateList.GoToAsync(AcmeId);
        Assert.True(await templateList.IsActiveAsync(templateName), "Expected newly created template to be Active");
        await templateList.DeactivateAsync(templateName);

        Assert.False(await templateList.HasItemAsync(templateName),
            "Expected deactivated template to be hidden from the default active-only list");

        await templateList.ShowInactiveAsync();

        Assert.True(await templateList.HasItemAsync(templateName),
            "Expected deactivated template to appear when 'Show inactive' is enabled");
    }

    [Fact]
    public async Task OnboardingTemplateList_ShowsDefaultBadgeForSeededTemplate()
    {
        var login        = new LoginPage(_page, _fixture.WebBaseUrl);
        var templateList = new OnboardingTemplateListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await templateList.GoToAsync(AcmeId);

        await templateList.ExpectDefaultBadgeAsync(SeededTemplateName, expected: true);
    }

    [Fact]
    public async Task SetAsDefault_SwitchesDefaultBadgeToSelectedTemplate()
    {
        var templateName = $"E2E Onboarding Default {Guid.NewGuid().ToString("N")[..8]}";

        var login        = new LoginPage(_page, _fixture.WebBaseUrl);
        var templateList = new OnboardingTemplateListPage(_page, _fixture.WebBaseUrl);
        var templateEdit = new OnboardingTemplateEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await templateList.GoToAsync(AcmeId);

        await templateEdit.GoToNewAsync(AcmeId);
        await templateEdit.FillNameAsync(templateName);
        await templateEdit.SaveAsync();

        try
        {
            await templateList.GoToAsync(AcmeId);
            await templateList.ExpectDefaultBadgeAsync(templateName, expected: false);

            await templateList.SetAsDefaultAsync(templateName);

            await templateList.ExpectDefaultBadgeAsync(templateName, expected: true);
            await templateList.ExpectDefaultBadgeAsync(SeededTemplateName, expected: false);
        }
        finally
        {
            await templateList.GoToAsync(AcmeId);
            await templateList.SetAsDefaultAsync(SeededTemplateName);
            await templateList.ExpectDefaultBadgeAsync(SeededTemplateName, expected: true);
        }
    }

    [Fact]
    public async Task DeactivateDefaultTemplate_IsRejectedAndTemplateStaysActive()
    {
        var login        = new LoginPage(_page, _fixture.WebBaseUrl);
        var templateList = new OnboardingTemplateListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await templateList.GoToAsync(AcmeId);
        await templateList.ExpectDefaultBadgeAsync(SeededTemplateName, expected: true);

        await templateList.DeactivateAsync(SeededTemplateName);

        var error = await templateList.WaitForActionErrorAsync();
        Assert.Contains("default onboarding template", error);

        await templateList.GoToAsync(AcmeId);
        Assert.True(await templateList.IsActiveAsync(SeededTemplateName),
            "Expected the default template to remain active after the rejected deactivation");
    }

    [Fact]
    public async Task NewPositionProfile_PreselectsDefaultOnboardingTemplate()
    {
        var templateName = $"E2E Onboarding Preselect {Guid.NewGuid().ToString("N")[..8]}";

        var login        = new LoginPage(_page, _fixture.WebBaseUrl);
        var templateList = new OnboardingTemplateListPage(_page, _fixture.WebBaseUrl);
        var templateEdit = new OnboardingTemplateEditPage(_page, _fixture.WebBaseUrl);
        var ppEdit       = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await templateList.GoToAsync(AcmeId);
        await templateList.ExpectDefaultBadgeAsync(SeededTemplateName, expected: true);

        await ppEdit.GoToNewAsync(AcmeId);
        await ppEdit.ExpectOnboardingTemplateSelectedAsync(SeededTemplateName);

        await templateEdit.GoToNewAsync(AcmeId);
        await templateEdit.FillNameAsync(templateName);
        await templateEdit.SaveAsync();

        try
        {
            await templateList.GoToAsync(AcmeId);
            await templateList.SetAsDefaultAsync(templateName);
            await templateList.ExpectDefaultBadgeAsync(templateName, expected: true);

            await ppEdit.GoToNewAsync(AcmeId);
            await ppEdit.ExpectOnboardingTemplateSelectedAsync(templateName);
        }
        finally
        {
            await templateList.GoToAsync(AcmeId);
            await templateList.SetAsDefaultAsync(SeededTemplateName);
            await templateList.ExpectDefaultBadgeAsync(SeededTemplateName, expected: true);
        }
    }
}
