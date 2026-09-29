using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

internal static class PositionProfileTestHelpers
{
    public static async Task<string> CreateUniquePositionProfileAsync(
        IPage page,
        string webBaseUrl,
        Guid companyId,
        LoginPage login,
        string hrAdminEmail,
        string returnToEmail,
        string titlePrefix = "E2E Vacancy Profile")
    {
        var title = $"{titlePrefix} {Guid.NewGuid():N}"[..Math.Min(60, titlePrefix.Length + 33)];

        var ppList = new PositionProfileListPage(page, webBaseUrl);
        var ppEdit = new PositionProfileEditPage(page, webBaseUrl);

        await login.SwitchAccountAsync(hrAdminEmail);

        await ppList.GoToAsync(companyId);
        await ppList.ClickNewPositionProfileAsync();
        await ppEdit.FillTitleAsync(title);
        await ppEdit.SelectDepartmentAsync("Engineering");
        await ppEdit.SelectLocationAsync("London Office");
        await ppEdit.SelectDefaultLeavePolicyAsync("Standard");
        await ppEdit.SaveAsync();

        await login.SwitchAccountAsync(returnToEmail);

        return title;
    }
}
