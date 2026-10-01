using HR.Modules.Reporting.ReportRegistry;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.GetReportCatalog;

internal sealed class GetReportCatalogHandler
{
    public Task<Result<GetReportCatalogResponse>> HandleAsync(
        GetReportCatalogRequest request,
        bool canViewRecruitment,
        bool canViewHr,
        bool canViewEmployeeStarter,
        bool canViewLeaveSummary,
        bool canViewProbation,
        bool canViewOnboarding,
        bool canViewWorkloadActions,
        bool canViewEqualityDiversity,
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
            canViewEqualityDiversity);

        var items = ReportCatalog.All
            .Where(definition => gates.IsAuthorized(definition.AccessGate))
            .Select(definition => new ReportCatalogItem(
                definition.Id, definition.DisplayName, definition.Category.ToString(), definition.Description, definition.ManagerReport))
            .ToList();

        return Task.FromResult(Result.Success(new GetReportCatalogResponse(items)));
    }
}
