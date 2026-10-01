using System.Text.RegularExpressions;

namespace HR.Web.Tests;

public class AccessibleNameSourceTests
{
    private static readonly string PagesRoot = Path.Combine("src", "HR.Web", "Components", "Pages");

    public static TheoryData<string, string> NamedControls => new()
    {
        { Path.Combine(PagesRoot, "Employees", "EmployeeList.razor"), "employee-list-search" },
        { Path.Combine(PagesRoot, "Employees", "EmployeeDirectory.razor"), "directory-search-input" },
        { Path.Combine(PagesRoot, "Employees", "EmployeeDirectory.razor"), "directory-department-filter" },
        { Path.Combine(PagesRoot, "Employees", "EmployeeDirectory.razor"), "directory-location-filter" },
        { Path.Combine(PagesRoot, "Users", "UserAdministrationList.razor"), "user-admin-search" },
        { Path.Combine(PagesRoot, "Reporting", "ReportCatalogPage.razor"), "report-catalog-search" },
        { Path.Combine("src", "HR.Web", "Components", "Layout", "MainLayout.razor"), "dev-persona-select" },
        { Path.Combine("src", "HR.Web", "Components", "Shared", "AdminQuickNav.razor"), "Search employees" },
    };

    [Theory]
    [MemberData(nameof(NamedControls))]
    public void Control_Has_A_Programmatic_Name(string relativePath, string idOrName)
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath));

        var hasAssociatedLabel = source.Contains($"for=\"{idOrName}\"", StringComparison.Ordinal)
            && (source.Contains($"[\"id\"] = \"{idOrName}\"", StringComparison.Ordinal)
                || source.Contains($"ID=\"{idOrName}\"", StringComparison.Ordinal));
        var hasAriaLabel = source.Contains($"aria-label=\"{idOrName}\"", StringComparison.Ordinal);

        Assert.True(hasAssociatedLabel || hasAriaLabel,
            $"{relativePath} must give '{idOrName}' a <label for> with a matching id, or an aria-label.");
    }

    [Theory]
    [InlineData("Components/Controls/ReportFilterPanel.razor")]
    [InlineData("Components/Pages/Employees/EmployeeEdit.razor")]
    [InlineData("Components/Pages/Employees/EmployeeEmploymentTab.razor")]
    public void Form_Labels_Are_Associated_With_Their_Control(string relativePath)
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "HR.Web", relativePath));

        var unassociated = Regex.Matches(source, "<label class=\"form-label[^\"]*\">")
            .Select(m => m.Value)
            .ToList();

        Assert.Empty(unassociated);
    }

    [Theory]
    [InlineData("Components/Controls/ReportFilterPanel.razor")]
    [InlineData("Components/Pages/Employees/EmployeeEdit.razor")]
    [InlineData("Components/Pages/Employees/EmployeeEmploymentTab.razor")]
    public void Required_Asterisks_Are_Hidden_From_Assistive_Technology(string relativePath)
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "HR.Web", relativePath));

        Assert.DoesNotContain("<span class=\"text-danger\">*</span>", source.Replace("Fields marked <span class=\"text-danger\">*</span> are required.", string.Empty));
    }

    [Fact]
    public void Report_Catalogue_Card_Is_A_Native_Link_Not_A_Clickable_Div()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), PagesRoot, "Reporting", "ReportCatalogPage.razor"));

        Assert.Contains("<a class=\"report-catalog-link stretched-link\"", source);
        Assert.DoesNotContain("@onclick=\"@(() => OnCardClicked", source);
        Assert.Contains("aria-pressed", source);
    }

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
