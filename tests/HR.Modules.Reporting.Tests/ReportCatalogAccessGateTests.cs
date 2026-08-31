using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.ReportRegistry;

namespace HR.Modules.Reporting.Tests;

/// <summary>
/// REP-04: pins the catalogue's access gate for "workload-actions" to <see cref="ReportAccessGate.WorkloadActions"/>
/// so the report catalogue (menu visibility, via ReportAccessGateEvaluator/"reporting:view-workload-actions") and
/// the GetWorkloadActions/ExportWorkloadActions endpoints (Policies("reporting:view-workload-actions")) can never
/// silently drift onto different authorization gates.
/// </summary>
public class ReportCatalogAccessGateTests
{
    [Fact]
    public void WorkloadActions_Entry_Uses_The_Dedicated_WorkloadActions_AccessGate()
    {
        Assert.True(ReportCatalog.TryGet("workload-actions", out var definition));
        Assert.Equal(ReportAccessGate.WorkloadActions, definition.AccessGate);
    }

    [Fact]
    public void WorkloadActions_Entry_Is_Categorised_As_Hr()
    {
        Assert.True(ReportCatalog.TryGet("workload-actions", out var definition));
        Assert.Equal(ReportCategory.Hr, definition.Category);
    }

    // ── ADM-08 governance reporting hub ────────────────────────────────────

    [Theory]
    [InlineData("governance-user-activity")]
    [InlineData("governance-administrative-changes")]
    [InlineData("governance-security-events")]
    [InlineData("governance-compliance-status")]
    public void Governance_Entries_Use_The_Governance_AccessGate_And_Administration_Category(string reportId)
    {
        Assert.True(ReportCatalog.TryGet(reportId, out var definition));
        Assert.Equal(ReportAccessGate.Governance, definition.AccessGate);
        Assert.Equal(ReportCategory.Administration, definition.Category);
    }

    [Theory]
    [InlineData("governance-user-activity")]
    [InlineData("governance-administrative-changes")]
    [InlineData("governance-security-events")]
    [InlineData("governance-compliance-status")]
    public void Governance_Entries_Are_Marked_Sensitive(string reportId)
    {
        Assert.True(ReportCatalog.TryGet(reportId, out var definition));
        Assert.Equal(ReportSensitivity.Sensitive, definition.Sensitivity);
    }
}
