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
}
