using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class AssignManagerTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid MarcusId = Guid.Parse("30000000-0000-0000-0000-000000000006");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task AssignManager_SavesSuccessfully_AndReflectsOnAdminProfile()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, MarcusId);

        await empEdit.OpenEmploymentTabAsync();

        await empEdit.SelectManagerAsync("Laura Bennett");

        await empEdit.ClickSaveChangesAsync();

        Assert.False(await empEdit.HasErrorAsync(),
            "Expected no error after assigning a manager");

        await empEdit.GoToAsync(AcmeId, MarcusId);
        await empEdit.OpenEmploymentTabAsync();

        var content = await _page.ContentAsync();
        Assert.Contains("Laura Bennett", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClearManager_ViaNoManagerOption_PersistsAsUnset()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, MarcusId);
        await empEdit.OpenEmploymentTabAsync();
        await empEdit.SelectManagerAsync("Laura Bennett");
        await empEdit.ClickSaveChangesAsync();
        Assert.False(await empEdit.HasErrorAsync(), "Expected no error after assigning a manager");

        await empEdit.GoToAsync(AcmeId, MarcusId);
        await empEdit.OpenEmploymentTabAsync();
        Assert.Equal("Laura Bennett", await empEdit.GetSelectedManagerTextAsync());

        await empEdit.ClearManagerAsync();
        await empEdit.ClickSaveChangesAsync();
        Assert.False(await empEdit.HasErrorAsync(), "Expected no error after clearing the manager");

        await empEdit.GoToAsync(AcmeId, MarcusId);
        await empEdit.OpenEmploymentTabAsync();
        Assert.Equal("No Manager", await empEdit.GetSelectedManagerTextAsync());
    }
}
