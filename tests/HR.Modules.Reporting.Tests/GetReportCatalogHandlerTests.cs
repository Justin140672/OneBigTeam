using HR.Modules.Reporting.Features.GetReportCatalog;

namespace HR.Modules.Reporting.Tests;

public class GetReportCatalogHandlerTests
{
    private readonly GetReportCatalogHandler _handler = new();

    [Fact]
    public async Task HandleAsync_Returns_Empty_Catalog_When_No_Category_Access()
    {
        var request = new GetReportCatalogRequest(Guid.NewGuid());

        var result = await _handler.HandleAsync(request, canViewRecruitment: false, canViewHr: false, canViewEmployeeStarter: false, canViewLeaveSummary: false, canViewProbation: false, canViewOnboarding: false, canViewWorkloadActions: false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Returns_Only_Recruitment_Entries_When_Only_Recruitment_Access()
    {
        var request = new GetReportCatalogRequest(Guid.NewGuid());

        var result = await _handler.HandleAsync(request, canViewRecruitment: true, canViewHr: false, canViewEmployeeStarter: false, canViewLeaveSummary: false, canViewProbation: false, canViewOnboarding: false, canViewWorkloadActions: false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Items.Count);
        Assert.Contains(result.Value.Items, i => i.Id == "recruitment-pipeline-summary" && i.Category == "Recruitment");
        Assert.Contains(result.Value.Items, i => i.Id == "recruitment-pipeline-report" && i.Category == "Recruitment");
        Assert.Contains(result.Value.Items, i => i.Id == "vacancy-performance-report" && i.Category == "Recruitment");
    }

    [Fact]
    public async Task HandleAsync_Returns_Only_Hr_Entries_When_Only_Hr_Access()
    {
        var request = new GetReportCatalogRequest(Guid.NewGuid());

        var result = await _handler.HandleAsync(request, canViewRecruitment: false, canViewHr: true, canViewEmployeeStarter: false, canViewLeaveSummary: false, canViewProbation: false, canViewOnboarding: false, canViewWorkloadActions: false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(9, result.Value!.Items.Count);
        Assert.Contains(result.Value.Items, i => i.Id == "hr-headcount-summary" && i.Category == "Hr");
        Assert.Contains(result.Value.Items, i => i.Id == "employee-directory" && i.Category == "Hr");
        Assert.Contains(result.Value.Items, i => i.Id == "employee-leavers" && i.Category == "Hr");
        Assert.Contains(result.Value.Items, i => i.Id == "leave-calendar" && i.Category == "Hr");
        Assert.Contains(result.Value.Items, i => i.Id == "sickness-report" && i.Category == "Hr");
        Assert.Contains(result.Value.Items, i => i.Id == "offboarding-progress" && i.Category == "Hr");
        Assert.Contains(result.Value.Items, i => i.Id == "document-compliance" && i.Category == "Hr");
        Assert.Contains(result.Value.Items, i => i.Id == "document-acknowledgement" && i.Category == "Hr");
        Assert.Contains(result.Value.Items, i => i.Id == "asset-assignment" && i.Category == "Hr");
    }

    [Fact]
    public async Task HandleAsync_Recruiter_Without_WorkloadActionsAccess_Does_Not_See_WorkloadActions()
    {
        // Bug fix (OBT-721): canViewWorkloadActions was previously hardcoded to true for every
        // caller, so a pure Recruiter (recruitment + employee-starter access, but no HR/Manager
        // role) incorrectly saw the HR-category "Workload & HR Actions Report" in their catalog.
        var request = new GetReportCatalogRequest(Guid.NewGuid());

        var result = await _handler.HandleAsync(
            request,
            canViewRecruitment: true,
            canViewHr: false,
            canViewEmployeeStarter: true,
            canViewLeaveSummary: false,
            canViewProbation: false,
            canViewOnboarding: false,
            canViewWorkloadActions: false,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(result.Value!.Items, i => i.Id == "workload-actions");
        Assert.Contains(result.Value.Items, i => i.Id == "employee-starters" && i.Category == "Hr");
        Assert.Contains(result.Value.Items, i => i.Id == "recruitment-pipeline-summary" && i.Category == "Recruitment");
        Assert.Contains(result.Value.Items, i => i.Id == "recruitment-pipeline-report" && i.Category == "Recruitment");
        Assert.Contains(result.Value.Items, i => i.Id == "vacancy-performance-report" && i.Category == "Recruitment");
    }

    [Fact]
    public async Task HandleAsync_Includes_WorkloadActions_When_Access_Granted()
    {
        var request = new GetReportCatalogRequest(Guid.NewGuid());

        var result = await _handler.HandleAsync(
            request,
            canViewRecruitment: false,
            canViewHr: false,
            canViewEmployeeStarter: false,
            canViewLeaveSummary: false,
            canViewProbation: false,
            canViewOnboarding: false,
            canViewWorkloadActions: true,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("workload-actions", item.Id);
        Assert.Equal("Hr", item.Category);
    }

    [Fact]
    public async Task HandleAsync_Returns_Only_Probation_Entry_When_Only_Probation_Access()
    {
        var request = new GetReportCatalogRequest(Guid.NewGuid());

        var result = await _handler.HandleAsync(request, canViewRecruitment: false, canViewHr: false, canViewEmployeeStarter: false, canViewLeaveSummary: false, canViewProbation: true, canViewOnboarding: false, canViewWorkloadActions: false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("probation-report", item.Id);
        Assert.Equal("Hr", item.Category);
    }

    [Fact]
    public async Task HandleAsync_Returns_All_Categories_When_Full_Access()
    {
        var request = new GetReportCatalogRequest(Guid.NewGuid());

        var result = await _handler.HandleAsync(request, canViewRecruitment: true, canViewHr: true, canViewEmployeeStarter: true, canViewLeaveSummary: true, canViewProbation: true, canViewOnboarding: true, canViewWorkloadActions: true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(17, result.Value!.Items.Count);
        Assert.Contains(result.Value.Items, i => i.Id == "recruitment-pipeline-summary");
        Assert.Contains(result.Value.Items, i => i.Id == "hr-headcount-summary");
        Assert.Contains(result.Value.Items, i => i.Id == "employee-directory");
        Assert.Contains(result.Value.Items, i => i.Id == "employee-starters");
        Assert.Contains(result.Value.Items, i => i.Id == "employee-leavers");
        Assert.Contains(result.Value.Items, i => i.Id == "leave-summary");
        Assert.Contains(result.Value.Items, i => i.Id == "leave-calendar");
        Assert.Contains(result.Value.Items, i => i.Id == "sickness-report");
        Assert.Contains(result.Value.Items, i => i.Id == "recruitment-pipeline-report");
        Assert.Contains(result.Value.Items, i => i.Id == "vacancy-performance-report");
        Assert.Contains(result.Value.Items, i => i.Id == "probation-report");
        Assert.Contains(result.Value.Items, i => i.Id == "onboarding-progress");
        Assert.Contains(result.Value.Items, i => i.Id == "offboarding-progress");
        Assert.Contains(result.Value.Items, i => i.Id == "document-compliance");
        Assert.Contains(result.Value.Items, i => i.Id == "document-acknowledgement");
        Assert.Contains(result.Value.Items, i => i.Id == "asset-assignment");
        Assert.Contains(result.Value.Items, i => i.Id == "workload-actions");
    }
}
