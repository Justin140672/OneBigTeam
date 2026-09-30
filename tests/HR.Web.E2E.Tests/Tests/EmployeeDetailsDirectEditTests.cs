using Microsoft.Playwright;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class EmployeeDetailsDirectEditTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task DetailsTab_DirectEditByHrAdmin_PersistsAfterReload()
    {
        var unique          = Guid.NewGuid().ToString("N")[..8];
        var preferredName   = $"E2E Preferred {unique}";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        var employee = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "DirectEdit");

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, employee.Id);
        await Assertions.Expect(_page.GetByPlaceholder("work@company.com")).Not.ToHaveValueAsync("", new() { Timeout = 20_000 });

        await _page.GetByPlaceholder("Defaults to first name").FillAsync(preferredName);

        await empEdit.ClickSaveChangesAsync();

        await empEdit.GoToAsync(AcmeId, employee.Id);

        var value = await _page.GetByPlaceholder("Defaults to first name").InputValueAsync();
        Assert.Equal(preferredName, value);
    }
}
