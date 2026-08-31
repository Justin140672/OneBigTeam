using HR.Modules.Reporting.ReportRegistry;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.GetReportCatalog;

internal sealed class GetReportCatalogHandler
{
    // REP-03: the catalogue is now sourced from the central ReportRegistry.ReportCatalog rather
    // than maintaining its own static list, so SaveReportView/AddReportFavourite/GetReportViews/
    // GetReportFavourites see exactly the same set of reports, categories and access gates.
    public Task<Result<GetReportCatalogResponse>> HandleAsync(
        GetReportCatalogRequest request,
        bool canViewRecruitment,
        bool canViewHr,
        bool canViewEmployeeStarter,
        bool canViewLeaveSummary,
        bool canViewProbation,
        bool canViewOnboarding,
        bool canViewWorkloadActions,
        bool canViewGovernance,
        CancellationToken cancellationToken)
    {
        var gates = new ReportAccessGates(
            canViewRecruitment,
            canViewHr,
            canViewEmployeeStarter,
            canViewLeaveSummary,
            canViewProbation,
            canViewOnboarding,
            canViewWorkloadActions,
            canViewGovernance);

        var items = ReportCatalog.All
            .Where(definition => gates.IsAuthorized(definition.AccessGate))
            .Select(definition => new ReportCatalogItem(
                definition.Id, definition.DisplayName, definition.Category.ToString(), definition.Description))
            .ToList();

        return Task.FromResult(Result.Success(new GetReportCatalogResponse(items)));
    }
}
