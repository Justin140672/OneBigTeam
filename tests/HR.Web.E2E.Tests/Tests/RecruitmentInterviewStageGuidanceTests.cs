using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

[Collection("RecruitmentStageList")]
public sealed class RecruitmentInterviewStageGuidanceTests(RecruiterPersonaFixture fixture)
    : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = RecruitmentSeedApi.AcmeId;

    private const string MarcusEmail = "marcus.diallo@acme.example";

    private async Task LoginAsync()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
    }

    [Fact]
    public async Task StageList_ShowsMultipleInterviewRoundsGuidance()
    {
        await LoginAsync();
        var list = new RecruitmentStageListPage(_page, _fixture.WebBaseUrl);
        await list.GoToAsync(AcmeId);

        await Assertions.Expect(_page.GetByTestId("interview-stage-guidance")).ToContainTextAsync(
            "Need multiple interview rounds? Add an interview stage for each round. Candidates will progress through them in the order shown.");
        await Assertions.Expect(_page.GetByTestId("add-interview-stage")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task AddInterviewStage_OpensPrePopulatedForm_AndCancelReturnsToList()
    {
        await LoginAsync();
        var list = new RecruitmentStageListPage(_page, _fixture.WebBaseUrl);
        await list.GoToAsync(AcmeId);

        await _page.GetByTestId("add-interview-stage").ClickUntilUrlAsync(
            _page, url => Regex.IsMatch(url, "/recruitment-stages/add-interview$"));

        await Assertions.Expect(_page.GetByTestId("interview-stage-purpose")).ToHaveTextAsync("Interview");
        await Assertions.Expect(_page.GetByTestId("interview-stage-terminal")).ToHaveTextAsync("None");
        await Assertions.Expect(_page.GetByTestId("interview-stage-status")).ToHaveTextAsync("Active");
        await Assertions.Expect(_page.Locator("#stage-name")).ToHaveValueAsync(new Regex(@"\S+ Interview$|^Interview$"), new() { Timeout = 15_000 });

        await _page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickUntilUrlAsync(
            _page, url => Regex.IsMatch(url, "/recruitment-stages$"));
    }
}
