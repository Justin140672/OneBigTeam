using HR.Modules.Reporting.ReportRegistry;

namespace HR.Modules.Reporting.Tests;

public class ReportCatalogTests
{
    [Fact]
    public void TryGet_Returns_True_And_Definition_For_Known_Id()
    {
        var found = ReportCatalog.TryGet("employee-directory", out var definition);

        Assert.True(found);
        Assert.Equal("employee-directory", definition.Id);
        Assert.Equal(ReportAccessGate.Hr, definition.AccessGate);
    }

    [Fact]
    public void TryGet_Returns_False_For_Unknown_Id()
    {
        var found = ReportCatalog.TryGet("not-a-real-report", out var definition);

        Assert.False(found);
        Assert.Null(definition);
    }

    [Fact]
    public void TryGet_Is_Case_Insensitive()
    {
        var found = ReportCatalog.TryGet("EMPLOYEE-DIRECTORY", out var definition);

        Assert.True(found);
        Assert.Equal("employee-directory", definition.Id);
    }

    [Fact]
    public void TryGet_Returns_False_For_Null_Or_Empty_Id()
    {
        Assert.False(ReportCatalog.TryGet(string.Empty, out _));
    }

    [Fact]
    public void WorkloadActions_GroupBy_Has_Explicit_Override_Values()
    {
        var found = ReportCatalog.TryGet("workload-actions", out var definition);

        Assert.True(found);
        var groupBy = definition.Fields["GroupBy"];
        Assert.NotNull(groupBy);
        Assert.Equal(
            new[] { "ActionType", "AssignedUser", "Department", "DueDate" },
            groupBy!.OrderBy(v => v, StringComparer.Ordinal));
    }

    [Fact]
    public void LeaveSummary_GroupBy_Is_Restricted_To_Enum_Member_Names()
    {
        var found = ReportCatalog.TryGet("leave-summary", out var definition);

        Assert.True(found);
        var groupBy = definition.Fields["GroupBy"];
        Assert.NotNull(groupBy);
        Assert.Equal(
            new[] { "Department", "Employee", "LeaveType" },
            groupBy!.OrderBy(v => v, StringComparer.Ordinal));
    }

    [Fact]
    public void No_Definition_Exposes_CompanyId_As_A_Field()
    {
        foreach (var definition in ReportCatalog.All)
        {
            Assert.False(definition.Fields.ContainsKey("CompanyId"),
                $"'{definition.Id}' should not expose CompanyId as a supported filter field.");
        }
    }

    [Fact]
    public void Every_Definition_Has_Non_Empty_Id_DisplayName_And_Description()
    {
        foreach (var definition in ReportCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(definition.Id));
            Assert.False(string.IsNullOrWhiteSpace(definition.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(definition.Description));
        }
    }
}
