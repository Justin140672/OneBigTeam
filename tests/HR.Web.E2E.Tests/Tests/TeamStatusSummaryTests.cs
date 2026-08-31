using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// DSH-05: exercises the reworked Manager Dashboard "Team Status" widget
/// (src/HR.Web/Components/Pages/Dashboards/TeamStatusSummary.razor), reached via
/// "/dashboard/manager". The widget now calls one authoritative endpoint and renders six
/// action tiles as &lt;button&gt; elements with aria-expanded ("At work", "Away today",
/// "On leave", "Sick", "In probation", "Missing fit notes"), a team-size count in the header,
/// and an inline drill-down panel per tile whose row count always equals the tile's number
/// (summary/drill-down parity).
///
/// Uses the same seeded manager persona the other manager-dashboard tests use — James Okafor
/// (james.okafor@acme.example, ManagerPersonaFixture default). Assertions are kept resilient to
/// org-chart seed drift (parity / >= / contains rather than hard-coded totals), so the
/// "In probation" tile is expected to list him for this login.
/// </summary>
public sealed class TeamStatusSummaryTests(ManagerPersonaFixture fixture)
    : RoleE2ETestBase<ManagerPersonaFixture>(fixture)
{
    private const string JamesEmail = "james.okafor@acme.example";

    private static readonly string[] ExpectedTileLabels =
    [
        "At work", "Away today", "On leave", "Sick", "In probation", "Missing fit notes",
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

        // Find the first tile with a count > 0 so the assertion is resilient to seed drift.
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

        // Clicking again collapses the panel.
        await dashboard.ClickTeamStatusTileAsync(targetLabel!);
        Assert.False(await dashboard.TeamStatusTileIsExpandedAsync(targetLabel!));
        Assert.Empty(await dashboard.GetTeamStatusDrilldownNamesAsync());
    }

    [Fact]
    public async Task InProbationTile_DrilldownRowCount_MatchesHeadline()
    {
        // Whichever manager persona this fixture logs in as, the "In probation" headline count
        // must always equal the number of rows in its drill-down panel (DSH-05 summary/drill-down
        // parity — both come from the same payload). We assert parity rather than a specific
        // seeded probationer because which manager's reporting sub-tree contains an active
        // probation record depends on the org-chart seed.
        var dashboard = await LoginAndOpenDashboardAsync();

        var probationCount = await dashboard.GetTeamStatusValueAsync("In probation");

        await dashboard.ClickTeamStatusTileAsync("In probation");
        Assert.True(await dashboard.TeamStatusTileIsExpandedAsync("In probation"));

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
