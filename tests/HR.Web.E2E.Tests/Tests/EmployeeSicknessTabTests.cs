using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class EmployeeSicknessTabTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId      = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomWilliams = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task SicknessTab_IsVisible_On_Employee_Edit_Page()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, TomWilliams);

        await EmployeeEditPage.SelectOwningGroupAsync(_page, "Sickness");
        await EmployeeEditPage.SectionTab(_page, "Sickness").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        Assert.True(
            await EmployeeEditPage.IsSectionTabPresentAsync(_page, "Sickness"),
            "Expected a 'Sickness' tab on the employee edit page");
    }

    [Fact]
    public async Task SicknessTab_ShowsRecordHistoryGrid()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, TomWilliams);
        await empEdit.OpenSicknessTabAsync();

        Assert.True(await empEdit.HasSicknessGridAsync(),
            "Expected the Syncfusion sickness record history grid to be visible on the Sickness tab");
    }

    [Fact]
    public async Task RecordSickness_AppearsInGrid_WithActiveStatus()
    {
        var suffix   = Guid.NewGuid().ToString("N")[..8];
        var catName  = $"E2E Cold {suffix}";
        var startDate = new DateOnly(2026, 1, 15).AddDays(Random.Shared.Next(0, 300));
        var startDateGridText = startDate.ToString("dd MMM yyyy");

        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var catEdit  = new SicknessCategoryEditPage(_page, _fixture.WebBaseUrl);
        var empEdit  = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await catEdit.GoToNewAsync(AcmeId);
        await catEdit.FillNameAsync(catName);
        await catEdit.FillDisplayOrderAsync(1);
        await catEdit.SaveAsync();

        var employee = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "Sick", activate: true);
        await empEdit.GoToAsync(AcmeId, employee.Id);
        await empEdit.OpenSicknessTabAsync();

        await empEdit.OpenRecordSicknessDialogAsync();
        await empEdit.SelectRecordSicknessCategoryAsync(catName);
        await empEdit.FillRecordSicknessStartDateAsync(startDate.ToString("dd/MM/yyyy"));
        await empEdit.SubmitRecordSicknessAsync();

        Assert.False(await empEdit.HasRecordSicknessErrorAsync(),
            "Expected no error after recording a new sickness absence");

        var status = await empEdit.GetSicknessStatusBadgeForStartDateAsync(startDateGridText);
        Assert.Equal("Active", status);

        await empEdit.StartCloseSicknessRecordAsync(startDateGridText);
        await empEdit.FillCloseSicknessEndDateAsync(startDate.AddDays(1).ToString("dd/MM/yyyy"));
        await empEdit.SubmitCloseSicknessRecordAsync();
    }

    [Fact]
    public async Task CloseSicknessRecord_UpdatesStatus_ToClosed()
    {
        var suffix   = Guid.NewGuid().ToString("N")[..8];
        var catName  = $"E2E Flu {suffix}";
        var startDate = new DateOnly(2026, 1, 15).AddDays(Random.Shared.Next(300, 600));
        var startDateGridText = startDate.ToString("dd MMM yyyy");
        var endDate = startDate.AddDays(3);

        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var catEdit  = new SicknessCategoryEditPage(_page, _fixture.WebBaseUrl);
        var empEdit  = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await catEdit.GoToNewAsync(AcmeId);
        await catEdit.FillNameAsync(catName);
        await catEdit.FillDisplayOrderAsync(1);
        await catEdit.SaveAsync();

        var employee = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "Sick", activate: true);
        await empEdit.GoToAsync(AcmeId, employee.Id);
        await empEdit.OpenSicknessTabAsync();

        await empEdit.OpenRecordSicknessDialogAsync();
        await empEdit.SelectRecordSicknessCategoryAsync(catName);
        await empEdit.FillRecordSicknessStartDateAsync(startDate.ToString("dd/MM/yyyy"));
        await empEdit.SubmitRecordSicknessAsync();

        var openStatus = await empEdit.GetSicknessStatusBadgeForStartDateAsync(startDateGridText);
        Assert.Equal("Active", openStatus);

        await empEdit.StartCloseSicknessRecordAsync(startDateGridText);
        await empEdit.FillCloseSicknessEndDateAsync(endDate.ToString("dd/MM/yyyy"));
        await empEdit.SubmitCloseSicknessRecordAsync();

        await empEdit.ExpectSicknessStatusForStartDateAsync(startDateGridText, "Closed");
    }
}
