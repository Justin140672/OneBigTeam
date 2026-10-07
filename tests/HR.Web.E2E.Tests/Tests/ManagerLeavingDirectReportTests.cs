using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class ManagerLeavingDirectReportTests(ManagerPersonaFixture fixture) : RoleE2ETestBase<ManagerPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid NinaId = SeededE2eEmployees.DedicatedManagerNinaPatelId;
    private const string NinaEmail = SeededE2eEmployees.DedicatedManagerNinaPatelEmail;

    private static DateOnly LeavingDate => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14);

    [Fact]
    public async Task Manager_Sees_Offboarding_Exit_Tasks_For_A_Leaving_DirectReport_In_Their_Task_List()
    {
        var report = await E2eEmployeeApi.CreateAcmeEmployeeAsync(
            _fixture.ApiBaseUrl, "LeavingTasks", managerId: NinaId, activate: true);
        await E2eEmployeeApi.StartLeavingProcessAsync(_fixture.ApiBaseUrl, report.Id, LeavingDate);

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(NinaEmail);
        await profile.GoToAsync(AcmeId, NinaId);
        await profile.OpenTasksTabAsync();

        var titles = await profile.GetTaskTitlesAsync();

        Assert.Contains(titles, t =>
            t.StartsWith("Conduct exit interview", StringComparison.Ordinal)
            && t.Contains(report.LastName, StringComparison.Ordinal));
        Assert.Contains(titles, t =>
            t.StartsWith("Arrange handover and knowledge transfer", StringComparison.Ordinal)
            && t.Contains(report.LastName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task MyTeamRoster_Lists_A_Leaving_DirectReport_With_A_Leaving_Badge()
    {
        var report = await E2eEmployeeApi.CreateAcmeEmployeeAsync(
            _fixture.ApiBaseUrl, "LeavingRoster", managerId: NinaId, activate: true);
        await E2eEmployeeApi.StartLeavingProcessAsync(_fixture.ApiBaseUrl, report.Id, LeavingDate);

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var roster = new MyTeamRosterPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(NinaEmail);
        await roster.GoToAsync(AcmeId);

        await roster.SearchAsync(report.LastName);
        await roster.ExpectRowCountAsync(1);

        Assert.True(await roster.RowExistsAsync(report.Id));
        Assert.Equal("Leaving", await roster.GetStatusBadgeTextAsync(report.Id));
    }
}
