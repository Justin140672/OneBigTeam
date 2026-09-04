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

    // REP-06: every catalogue entry must carry an explicit sensitivity classification driving
    // export audit policy (HR.Modules.Reporting.Services.ReportExportAuditor).
    [Theory]
    [InlineData("employee-directory", "Sensitive")]
    [InlineData("employee-starters", "Sensitive")]
    [InlineData("employee-leavers", "Sensitive")]
    [InlineData("leave-summary", "Sensitive")]
    [InlineData("leave-calendar", "Sensitive")]
    [InlineData("sickness-report", "Sensitive")]
    [InlineData("probation-report", "Sensitive")]
    [InlineData("onboarding-progress", "Sensitive")]
    [InlineData("offboarding-progress", "Sensitive")]
    [InlineData("document-compliance", "Sensitive")]
    [InlineData("document-acknowledgement", "Sensitive")]
    [InlineData("asset-assignment", "Sensitive")]
    [InlineData("workload-actions", "Sensitive")]
    [InlineData("hr-headcount-summary", "Sensitive")]
    [InlineData("equality-diversity", "Standard")]
    [InlineData("recruitment-pipeline-summary", "Standard")]
    [InlineData("recruitment-pipeline-report", "Standard")]
    [InlineData("vacancy-performance-report", "Standard")]
    public void Definition_Has_Expected_Sensitivity_Classification(string reportId, string expectedSensitivity)
    {
        var found = ReportCatalog.TryGet(reportId, out var definition);

        Assert.True(found, $"Expected '{reportId}' to be present in the catalogue.");
        Assert.Equal(expectedSensitivity, definition.Sensitivity.ToString());
    }

    [Fact]
    public void Every_Definition_Has_A_Sensitivity_Of_Standard_Or_Sensitive()
    {
        foreach (var definition in ReportCatalog.All)
        {
            Assert.True(
                definition.Sensitivity is ReportSensitivity.Standard or ReportSensitivity.Sensitive,
                $"'{definition.Id}' has an unexpected sensitivity value: {definition.Sensitivity}.");
        }
    }

    [Fact]
    public void All_Catalogue_Entries_Are_Covered_By_The_Sensitivity_Classification_Above()
    {
        // Guards against a new report being added to the catalogue without also being added to the
        // classification test above (and, per the REP-06 spec, without a deliberate Sensitive/Standard
        // decision being made for it).
        var classifiedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "employee-directory", "employee-starters", "employee-leavers", "leave-summary", "leave-calendar",
            "sickness-report", "probation-report", "onboarding-progress", "offboarding-progress",
            "document-compliance", "document-acknowledgement", "asset-assignment", "workload-actions",
            "hr-headcount-summary", "recruitment-pipeline-summary", "recruitment-pipeline-report",
            "vacancy-performance-report", "equality-diversity",
        };

        var actualIds = ReportCatalog.All.Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(classifiedIds.OrderBy(x => x), actualIds.OrderBy(x => x));
    }
}
