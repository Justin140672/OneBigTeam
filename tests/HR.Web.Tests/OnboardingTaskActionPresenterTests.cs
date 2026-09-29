using HR.Web.Services;

namespace HR.Web.Tests;

public class OnboardingTaskActionPresenterTests
{
    private static readonly Guid CompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string ImportLink = "/companies/{companyId}/data-import/employees";

    [Fact]
    public void ImportEmployees_Incomplete_Shows_Primary_Then_Secondary_Download()
    {
        var actions = OnboardingTaskActionPresenter.Present("import-employees", ImportLink, CompanyId, isCompleted: false);

        Assert.Equal(2, actions.Count);

        Assert.Equal("Add or import employees", actions[0].Label);
        Assert.True(actions[0].IsPrimary);
        Assert.False(actions[0].IsDownload);
        Assert.Equal($"/companies/{CompanyId}/data-import/employees?returnUrl=%2Fgetting-started", actions[0].Href);

        Assert.Equal("Download import template", actions[1].Label);
        Assert.False(actions[1].IsPrimary);
        Assert.True(actions[1].IsDownload);
        Assert.Equal($"/companies/{CompanyId}/data-import/employees/template/download", actions[1].Href);
        Assert.Equal("fa-solid fa-download", actions[1].IconCss);
        Assert.Contains("file", actions[1].AccessibleName);
    }

    [Fact]
    public void ImportEmployees_Complete_Shows_No_Actions()
    {
        var actions = OnboardingTaskActionPresenter.Present("import-employees", ImportLink, CompanyId, isCompleted: true);

        Assert.Empty(actions);
    }

    [Fact]
    public void OtherTask_Incomplete_Shows_Single_Go_To_Task_Action_With_ReturnUrl()
    {
        var actions = OnboardingTaskActionPresenter.Present(
            "configure-hr-settings", "/companies/{companyId}/hr-settings", CompanyId, isCompleted: false);

        var action = Assert.Single(actions);
        Assert.Equal("Go to task", action.Label);
        Assert.Equal($"/companies/{CompanyId}/hr-settings?returnUrl=%2Fgetting-started", action.Href);
    }

    [Fact]
    public void Obsolete_Download_Task_Key_Gets_No_Special_Download_Label()
    {
        var actions = OnboardingTaskActionPresenter.Present(
            "download-employee-import-template", "/x", CompanyId, isCompleted: false);

        Assert.DoesNotContain(actions, a => a.Label == "Download");
    }
}
