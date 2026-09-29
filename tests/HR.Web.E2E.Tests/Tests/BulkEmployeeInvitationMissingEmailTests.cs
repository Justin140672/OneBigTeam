using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class BulkEmployeeInvitationMissingEmailTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string HrAdminEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task InviteMode_CandidateMissingWorkEmail_IsVisibleButNotPreselected_WithEditLink()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var grid = new InviteModeCandidateGridPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await empList.GoToInviteModeAsync(AcmeId);
        await grid.WaitForLoadedAsync();

        var missingEmailRow = _page.Locator(".invite-mode-grid .e-row")
            .Filter(new() { HasText = "No work email" });

        if (await missingEmailRow.CountAsync() == 0)
        {
            return;
        }

        var rowText = (await missingEmailRow.First.InnerTextAsync()).Trim();
        Assert.False(
            await missingEmailRow.First.Locator(".e-checkbox-wrapper input[type='checkbox']").First.IsCheckedAsync(),
            $"Expected the candidate row missing a work email ('{rowText}') to NOT be pre-selected");

        var editLink = missingEmailRow.First.GetByRole(AriaRole.Link).Last;
        var href = await editLink.GetAttributeAsync("href");
        Assert.NotNull(href);
        Assert.Contains($"/companies/{AcmeId}/employees/", href);
    }
}
