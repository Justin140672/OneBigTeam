using System.Text.RegularExpressions;

namespace HR.Web.Tests;

public class SwitcherSelectedStateSourceTests
{
    [Fact]
    public void DashboardSwitcher_Renders_Native_Buttons_Exposing_Pressed_State()
    {
        var source = ReadPage("Dashboards", "DashboardSwitcher.razor");

        Assert.Contains("<button type=\"button\"", source);
        Assert.Contains("aria-pressed=\"@(current ? \"true\" : \"false\")\"", source);
        Assert.DoesNotContain("<SfButton", source);
    }

    [Theory]
    [InlineData("Employees", "MyTeamWidget.razor")]
    [InlineData("Employees", "MyTeamRoster.razor")]
    public void TeamScopeSwitcher_Exposes_Pressed_State_On_Both_Buttons(string folder, string file)
    {
        var source = ReadPage(folder, file);

        var directButton = Regex.Match(source, @"<button[^\r\n]*>Direct Reports</button>");
        var allButton = Regex.Match(source, @"<button[^\r\n]*>All Reports</button>");

        Assert.True(directButton.Success, "Direct Reports must be a native button.");
        Assert.True(allButton.Success, "All Reports must be a native button.");
        Assert.Contains("aria-pressed=\"@(!_includeIndirect ? \"true\" : \"false\")\"", directButton.Value);
        Assert.Contains("aria-pressed=\"@(_includeIndirect ? \"true\" : \"false\")\"", allButton.Value);
    }

    [Fact]
    public void Pressed_Buttons_Have_A_Non_Colour_Cue_And_A_Dark_Theme_Style()
    {
        var css = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "HR.Web", "wwwroot", "app.css"));

        Assert.Contains(".dashboard-switcher-item[aria-pressed=\"true\"]", css);
        Assert.Contains(".team-scope-btn[aria-pressed=\"true\"]", css);
        Assert.Contains("html[data-theme=\"dark\"] .dashboard-switcher-item", css);
    }

    private static string ReadPage(string folder, string file) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "HR.Web", "Components", "Pages", folder, file));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src"))
                && (dir.GetFiles("*.sln").Length > 0 || dir.GetFiles("*.slnx").Length > 0))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }
}
