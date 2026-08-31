using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// ADM-07 — administrative wayfinding breadcrumbs. From an employment type edit page an HR admin
/// sees an "Administration" link and an "Employment Types" link, and the "Employment Types" crumb
/// returns to the list URL.
/// </summary>
public sealed class AdministrationBreadcrumbTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task EmploymentTypeEdit_ShowsAdministrationAndEmploymentTypesBreadcrumbs()
    {
        var typeName = $"E2E Crumb {Guid.NewGuid().ToString("N")[..8]}";

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var typeList = new EmploymentTypeListPage(_page, _fixture.WebBaseUrl);
        var typeEdit = new EmploymentTypeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await typeList.GoToAsync(AcmeId);
        await typeList.ClickNewAsync();
        await typeEdit.FillNameAsync(typeName);
        await typeEdit.FillDescriptionAsync("Created by E2E test");
        await typeEdit.SaveAsync();

        // Re-open the newly created type for editing.
        await typeList.GoToAsync(AcmeId);
        var href = await _page.Locator(".e-rowcell a").Filter(new() { HasText = typeName }).First.GetAttributeAsync("href");
        Assert.NotNull(href);
        await _page.GotoAsync($"{_fixture.WebBaseUrl}{href}");
        await _page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });

        var adminCrumb = _page.GetByRole(AriaRole.Link, new() { Name = "Administration" });
        var typesCrumb = _page.GetByRole(AriaRole.Link, new() { Name = "Employment Types" });

        await Assertions.Expect(adminCrumb).ToBeVisibleAsync();
        await Assertions.Expect(typesCrumb).ToBeVisibleAsync();

        await typesCrumb.ClickAsync();

        await _page.WaitForURLAsync($"**/companies/{AcmeId}/employment-types", new() { Timeout = 30_000 });
        Assert.EndsWith($"/companies/{AcmeId}/employment-types", _page.Url.TrimEnd('/'));
    }

    [Fact]
    public async Task Breadcrumb_FromListToEditAndBack_LandsOnListUrl()
    {
        var typeName = $"E2E Crumb {Guid.NewGuid().ToString("N")[..8]}";

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var typeList = new EmploymentTypeListPage(_page, _fixture.WebBaseUrl);
        var typeEdit = new EmploymentTypeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await typeList.GoToAsync(AcmeId);
        await typeList.ClickNewAsync();
        await typeEdit.FillNameAsync(typeName);
        await typeEdit.SaveAsync();

        await typeList.GoToAsync(AcmeId);
        var href = await _page.Locator(".e-rowcell a").Filter(new() { HasText = typeName }).First.GetAttributeAsync("href");
        Assert.NotNull(href);
        await _page.GotoAsync($"{_fixture.WebBaseUrl}{href}");
        await _page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });

        await _page.GetByRole(AriaRole.Link, new() { Name = "Employment Types" }).ClickAsync();

        await _page.WaitForURLAsync($"**/companies/{AcmeId}/employment-types", new() { Timeout = 30_000 });
        Assert.EndsWith($"/companies/{AcmeId}/employment-types", _page.Url.TrimEnd('/'));
    }
}
