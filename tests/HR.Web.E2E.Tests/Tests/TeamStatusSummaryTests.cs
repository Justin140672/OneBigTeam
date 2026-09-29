using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class TeamStatusSummaryTests(ManagerPersonaFixture fixture)
    : RoleE2ETestBase<ManagerPersonaFixture>(fixture)
{
    private const string JamesEmail = "james.okafor@acme.example";

    private static readonly string[] ExpectedTileLabels =
    [
        "At work", "Away today", "On leave", "Sick", "On probation", "Missing fit notes",
    ];

    private async Task<ManagerDashboardPage> LoginAndOpenDashboardAsync()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.HasWidgetAsync("Team Status"));
        await dashboard.WaitForTeamStatusLoadedAsync();
        return dashboard;
    }

    [Fact]
    public async Task TeamStatusWidget_ShowsSixLabelledTiles_AndTeamSizeCount()
    {
        var dashboard = await LoginAndOpenDashboardAsync();

        Assert.False(await dashboard.TeamStatusIsEmptyAsync(),
            "Expected James Okafor to have direct reports, not the empty state.");

        var labels = await dashboard.GetTeamStatusTileLabelsAsync();
        foreach (var expected in ExpectedTileLabels)
            Assert.Contains(expected, labels);

        var teamSize = await dashboard.GetTeamStatusHeaderCountAsync();
        Assert.True(teamSize >= 1, $"Expected a team-size count of at least 1, got {teamSize}.");
    }

    [Fact]
    public async Task EveryTile_HasNonNegativeIntegerValue_AndIsAKeyboardFocusableButton()
    {
        var dashboard = await LoginAndOpenDashboardAsync();

        foreach (var label in ExpectedTileLabels)
        {
            var value = await dashboard.GetTeamStatusValueAsync(label);
            Assert.True(value >= 0, $"Tile '{label}' should show a non-negative integer, got {value}.");

            Assert.Equal("button", await dashboard.GetTeamStatusTileTagNameAsync(label));
            Assert.True(await dashboard.TeamStatusTileIsKeyboardFocusableAsync(label),
                $"Tile '{label}' should be keyboard-focusable.");
            Assert.False(await dashboard.TeamStatusTileIsExpandedAsync(label),
                $"Tile '{label}' should start collapsed (aria-expanded=false).");
        }
    }

    [Fact]
    public async Task ClickingTileWithMembers_OpensDrilldownWithParity_AndTogglesClosed()
    {
        var dashboard = await LoginAndOpenDashboardAsync();

        string? targetLabel = null;
        var targetCount = 0;
        foreach (var label in ExpectedTileLabels)
        {
            var value = await dashboard.GetTeamStatusValueAsync(label);
            if (value > 0)
            {
                targetLabel = label;
                targetCount = value;
                break;
            }
        }

        Assert.NotNull(targetLabel);

        await dashboard.ClickTeamStatusTileAsync(targetLabel!);
        Assert.True(await dashboard.TeamStatusTileIsExpandedAsync(targetLabel!));

        var names = await dashboard.GetTeamStatusDrilldownNamesAsync();
        Assert.Equal(targetCount, names.Count);

        await dashboard.ClickTeamStatusTileAsync(targetLabel!);
        Assert.False(await dashboard.TeamStatusTileIsExpandedAsync(targetLabel!));
        Assert.Empty(await dashboard.GetTeamStatusDrilldownNamesAsync());
    }

    [Fact]
    public async Task InProbationTile_DrilldownRowCount_MatchesHeadline()
    {
        var dashboard = await LoginAndOpenDashboardAsync();

        var probationCount = await dashboard.GetTeamStatusValueAsync("On probation");

        await dashboard.ClickTeamStatusTileAsync("On probation");
        Assert.True(await dashboard.TeamStatusTileIsExpandedAsync("On probation"));

        var names = await dashboard.GetTeamStatusDrilldownNamesAsync();
        Assert.Equal(probationCount, names.Count);
    }

    [Fact]
    public async Task AtWorkCount_IsConsistentWithTeamSize()
    {
        var dashboard = await LoginAndOpenDashboardAsync();

        var teamSize = await dashboard.GetTeamStatusHeaderCountAsync();
        var atWork   = await dashboard.GetTeamStatusValueAsync("At work");
        var awayToday = await dashboard.GetTeamStatusValueAsync("Away today");

        Assert.True(atWork <= teamSize,
            $"'At work' ({atWork}) cannot exceed the team size ({teamSize}).");
        Assert.True(awayToday <= teamSize,
            $"'Away today' ({awayToday}) cannot exceed the team size ({teamSize}).");
        Assert.True(atWork + awayToday <= teamSize,
            $"'At work' ({atWork}) + 'Away today' ({awayToday}) cannot exceed the team size ({teamSize}).");
    }
}
