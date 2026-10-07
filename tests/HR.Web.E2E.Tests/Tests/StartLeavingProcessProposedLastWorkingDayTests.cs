using System.Globalization;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class StartLeavingProcessProposedLastWorkingDayTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private const string LauraEmail = "laura.bennett@acme.example";

    private const int MondayTuesdayWednesday = 1 | 2 | 4;

    private static DateOnly FirstOnOrAfter(int daysAhead, DayOfWeek day)
    {
        var date = DateOnly.FromDateTime(DateTime.Today).AddDays(daysAhead);
        while (date.DayOfWeek != day)
            date = date.AddDays(1);
        return date;
    }

    private static string Format(DateOnly date) =>
        date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private async Task<StartLeavingProcessDialog> OpenWizardOnLeavingDateStepAsync(Guid employeeId)
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var dialog = new StartLeavingProcessDialog(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await empEdit.GoToAsync(E2eEmployeeApi.AcmeId, employeeId);

        await dialog.OpenAsync();
        await dialog.FillResignationReceivedDateAsync(E2eDates.DaysFromToday(0));
        await dialog.ClickNextAsync();

        return dialog;
    }

    private static async Task<Guid> CreateEmployeeAsync(string apiBaseUrl, string prefix) =>
        (await E2eEmployeeApi.CreateAcmeEmployeeAsync(apiBaseUrl, prefix, activate: true)).Id;

    [Fact]
    public async Task ProposedLastWorkingDay_ForStandardEmployee_WithWeekendLeavingDate_IsPreviousFriday()
    {
        var employeeId = await CreateEmployeeAsync(_fixture.ApiBaseUrl, "LwdWeekend");
        var saturday = FirstOnOrAfter(420, DayOfWeek.Saturday);

        var dialog = await OpenWizardOnLeavingDateStepAsync(employeeId);
        await dialog.FillLeavingDateAsync(Format(saturday));
        await dialog.ClickNextAsync();

        Assert.Equal("3. Last Working Day", await dialog.GetActiveStepLabelAsync());
        await dialog.ExpectLastWorkingDayAsync(Format(saturday.AddDays(-1)));
    }

    [Fact]
    public async Task ProposedLastWorkingDay_WhenLeavingDateIsBankHoliday_IsPreviousWorkingDay()
    {
        var employeeId = await CreateEmployeeAsync(_fixture.ApiBaseUrl, "LwdBankHol");
        var holiday = FirstOnOrAfter(430, DayOfWeek.Wednesday);
        await E2eEmployeeApi.EnsurePublicHolidayAsync(_fixture.ApiBaseUrl, holiday, "E2E Proposed Last Working Day Holiday");

        var dialog = await OpenWizardOnLeavingDateStepAsync(employeeId);
        await dialog.FillLeavingDateAsync(Format(holiday));
        await dialog.ClickNextAsync();

        Assert.Equal("3. Last Working Day", await dialog.GetActiveStepLabelAsync());
        await dialog.ExpectLastWorkingDayAsync(Format(holiday.AddDays(-1)));
    }

    [Fact]
    public async Task ProposedLastWorkingDay_ForPartTimeEmployee_SkipsDaysOutsideWorkPattern()
    {
        var employeeId = await CreateEmployeeAsync(_fixture.ApiBaseUrl, "LwdPartTime");
        await E2eEmployeeApi.SetWorkingPatternAsync(_fixture.ApiBaseUrl, employeeId, MondayTuesdayWednesday);
        var friday = FirstOnOrAfter(440, DayOfWeek.Friday);

        var dialog = await OpenWizardOnLeavingDateStepAsync(employeeId);
        await dialog.FillLeavingDateAsync(Format(friday));
        await dialog.ClickNextAsync();

        Assert.Equal("3. Last Working Day", await dialog.GetActiveStepLabelAsync());
        await dialog.ExpectLastWorkingDayAsync(Format(friday.AddDays(-2)));
    }

    [Fact]
    public async Task ProposedLastWorkingDay_ManuallyEditedValue_IsNotOverwrittenAfterBackAndLeavingDateChange()
    {
        var employeeId = await CreateEmployeeAsync(_fixture.ApiBaseUrl, "LwdManual");
        var saturday = FirstOnOrAfter(450, DayOfWeek.Saturday);
        var manualLastWorkingDay = saturday.AddDays(-2);

        var dialog = await OpenWizardOnLeavingDateStepAsync(employeeId);
        await dialog.FillLeavingDateAsync(Format(saturday));
        await dialog.ClickNextAsync();
        await dialog.ExpectLastWorkingDayAsync(Format(saturday.AddDays(-1)));

        await dialog.FillLastWorkingDayAsync(Format(manualLastWorkingDay));
        await dialog.ExpectLastWorkingDayAsync(Format(manualLastWorkingDay));

        await dialog.ClickBackAsync();
        await dialog.FillLeavingDateAsync(Format(saturday.AddDays(1)));
        await dialog.ClickNextAsync();

        Assert.Equal("3. Last Working Day", await dialog.GetActiveStepLabelAsync());
        await dialog.ExpectLastWorkingDayAsync(Format(manualLastWorkingDay));
    }
}
