using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Accessibility of the Recruitment Stages list and edit pages. Read-only against the shared stage
/// list, but kept in the "RecruitmentStageList" collection so it never observes
/// RecruitmentStageManagementTests mid-mutation.
/// </summary>
[Collection("RecruitmentStageList")]
public sealed class RecruitmentStageAccessibilityTests(RecruiterPersonaFixture fixture)
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
    public async Task StageList_SearchIsNamed_AndAnnouncesResultCount()
    {
        await LoginAsync();
        await new RecruitmentStageListPage(_page, _fixture.WebBaseUrl).GoToAsync(AcmeId);

        var search = _page.GetByRole(AriaRole.Textbox, new() { Name = "Search recruitment stages", Exact = true });
        await Assertions.Expect(search).ToBeVisibleAsync();

        await search.FillAsync($"zzz-no-match-{Guid.NewGuid():N}");
        await _page.Keyboard.PressAsync("Tab");

        await Assertions.Expect(_page.GetByTestId("search-results-announcer").First)
            .ToHaveTextAsync("No stages found", new() { Timeout = 20_000 });
    }

    [Fact]
    public async Task StageList_MoveButtons_NameTheStageTheyMove()
    {
        await LoginAsync();
        var list = new RecruitmentStageListPage(_page, _fixture.WebBaseUrl);
        await list.GoToAsync(AcmeId);

        var names = (await list.GetNamesInOrderAsync()).Where(n => n.Length > 0).ToList();
        Assert.NotEmpty(names);

        foreach (var name in names)
        {
            await Assertions.Expect(_page.GetByRole(AriaRole.Button, new() { Name = $"Move {name} up", Exact = true }))
                .ToHaveCountAsync(1);
            await Assertions.Expect(_page.GetByRole(AriaRole.Button, new() { Name = $"Move {name} down", Exact = true }))
                .ToHaveCountAsync(1);
        }

        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new() { Name = $"Move {names[0]} up", Exact = true }))
            .ToBeDisabledAsync();
    }

    [Fact]
    public async Task StageForm_FieldsAreLabelled_AndRequiredControlsAreMarked()
    {
        await LoginAsync();
        await new RecruitmentStageEditPage(_page, _fixture.WebBaseUrl).GoToNewAsync(AcmeId);

        await Assertions.Expect(_page.GetByLabel("Name")).ToHaveAttributeAsync("id", "stage-name");
        await A11yAssert.LabelledAsync(_page, "stage-terminal-outcome", "Terminal Outcome");
        await A11yAssert.LabelledAsync(_page, "stage-purpose", "Purpose");

        await A11yAssert.RequiredAsync(_page.Locator("#stage-name"));
        await A11yAssert.RequiredAsync(_page.Locator("#stage-terminal-outcome"));
        await Assertions.Expect(_page.Locator("#stage-purpose")).Not.ToHaveAttributeAsync("aria-required", "true");

        await Assertions.Expect(_page.Locator("#stage-terminal-outcome"))
            .ToHaveAttributeAsync("aria-describedby", new System.Text.RegularExpressions.Regex("stage-terminal-outcome-help"));
        await Assertions.Expect(_page.Locator("#stage-terminal-outcome-help")).ToContainTextAsync("Hired");
    }

    [Fact]
    public async Task StageForm_EmptySubmit_AssociatesErrorAndFocusesFirstInvalid()
    {
        await LoginAsync();
        await new RecruitmentStageEditPage(_page, _fixture.WebBaseUrl).GoToNewAsync(AcmeId);

        await _page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        await A11yAssert.InvalidWithAssociatedMessageAsync(_page, _page.Locator("#stage-name"));
        await A11yAssert.FocusIsInsideFieldAsync(_page, "Name");
    }

    [Theory]
    [InlineData("list")]
    [InlineData("new")]
    public async Task StagePages_HaveNoControlNamedWithAGenericWidgetType(string page)
    {
        await LoginAsync();
        if (page == "list")
            await new RecruitmentStageListPage(_page, _fixture.WebBaseUrl).GoToAsync(AcmeId);
        else
            await new RecruitmentStageEditPage(_page, _fixture.WebBaseUrl).GoToNewAsync(AcmeId);
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await A11yAssert.NoGenericControlNamesAsync(_page, $"recruitment stage {page}");
    }
}
