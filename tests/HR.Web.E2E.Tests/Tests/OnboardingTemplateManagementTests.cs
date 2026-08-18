using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the onboarding template edit workflow: creating a template with a checklist task
/// and confirming edits to its name/task title persist server-side across a reload.
/// </summary>
public sealed class OnboardingTemplateManagementTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task EditOnboardingTemplate_PersistsAcrossReload()
    {
        var originalName = $"E2E Onboarding {Guid.NewGuid().ToString("N")[..8]}";
        var updatedName = $"{originalName} Updated";
        var originalTaskTitle = $"E2E Task {Guid.NewGuid().ToString("N")[..8]}";
        var updatedTaskTitle = $"{originalTaskTitle} Updated";

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var templateEdit = new OnboardingTemplateEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Create a template with a checklist task.
        await templateEdit.GoToNewAsync(AcmeId);
        await templateEdit.FillNameAsync(originalName);
        await templateEdit.FillDescriptionAsync("Created by E2E test");
        await templateEdit.ClickAddTaskAsync();
        await templateEdit.FillTaskTitleAsync(originalTaskTitle);
        await templateEdit.SaveAsync();

        // Locate the newly created template in the list and navigate to its edit page.
        await _page.WaitForSelectorAsync(".e-grid .e-row, .e-grid .e-emptyrow", new() { Timeout = 15_000 });
        var href = await _page.Locator(".e-rowcell a").Filter(new() { HasText = originalName }).First.GetAttributeAsync("href");
        Assert.NotNull(href);
        await _page.GotoAsync($"{_fixture.WebBaseUrl}{href}");
        await _page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });

        // Edit the name and the existing task's title, then save.
        await templateEdit.FillNameAsync(updatedName);
        await templateEdit.FillTaskTitleAsync(updatedTaskTitle);
        await templateEdit.SaveAsync();

        // Navigate back to the updated template via the list and reload to confirm persistence.
        await _page.WaitForSelectorAsync(".e-grid .e-row, .e-grid .e-emptyrow", new() { Timeout = 15_000 });
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

        // Create first
        await templateEdit.GoToNewAsync(AcmeId);
        await templateEdit.FillNameAsync(templateName);
        await templateEdit.SaveAsync();

        // Now deactivate
        await templateList.GoToAsync(AcmeId);
        Assert.True(await templateList.IsActiveAsync(templateName), "Expected newly created template to be Active");
        await templateList.DeactivateAsync(templateName);

        Assert.False(await templateList.HasItemAsync(templateName),
            "Expected deactivated template to be hidden from the default active-only list");

        // Show inactive and verify
        await templateList.ShowInactiveAsync();

        Assert.True(await templateList.HasItemAsync(templateName),
            "Expected deactivated template to appear when 'Show inactive' is enabled");
    }
}
