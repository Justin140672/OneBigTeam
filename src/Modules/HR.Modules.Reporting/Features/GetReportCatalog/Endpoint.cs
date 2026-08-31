using FastEndpoints;
using HR.Modules.Reporting.ReportRegistry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetReportCatalog;

internal sealed class Endpoint(
    GetReportCatalogHandler handler,
    IAuthorizationService authorizationService) : Endpoint<GetReportCatalogRequest, GetReportCatalogResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/catalog");
        // Baseline access to the reporting area. Users without this fail outright with 403.
        // Category-level visibility (recruitment vs hr vs the combined employee-starter/
        // leave-summary policies) is filtered inside the handler for users who do have baseline
        // access but only some category sub-policies — mirrors GetEmployeeTimeline's callerIsHr
        // pattern.
        Policies("reporting:view");
    }

    public override async Task HandleAsync(
        GetReportCatalogRequest request,
        CancellationToken cancellationToken)
    {
        // Bug fix retained: workload-actions is gated on a dedicated Manager/HrAdministrator-only
        // policy (reporting:view-workload-actions) rather than the plain Category-based split — see
        // IdentityModule.cs — so a Recruiter with no HR/Manager role never sees it.
        var gates = await ReportAccessGateEvaluator.EvaluateAsync(authorizationService, User);

        var result = await handler.HandleAsync(
            request,
            gates.CanViewRecruitment,
            gates.CanViewHr,
            gates.CanViewEmployeeStarter,
            gates.CanViewLeaveSummary,
            gates.CanViewProbation,
            gates.CanViewOnboarding,
            gates.CanViewWorkloadActions,
            gates.CanViewGovernance,
            cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
