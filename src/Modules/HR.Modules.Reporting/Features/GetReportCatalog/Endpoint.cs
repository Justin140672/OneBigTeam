using FastEndpoints;
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
        var canViewRecruitment = (await authorizationService.AuthorizeAsync(User, "reporting:view-recruitment")).Succeeded;
        var canViewHr = (await authorizationService.AuthorizeAsync(User, "reporting:view-hr")).Succeeded;
        var canViewEmployeeStarter = (await authorizationService.AuthorizeAsync(User, "reporting:view-employee-starter")).Succeeded;
        var canViewLeaveSummary = (await authorizationService.AuthorizeAsync(User, "reporting:view-leave-summary")).Succeeded;
        var canViewProbation = (await authorizationService.AuthorizeAsync(User, "reporting:view-probation")).Succeeded;
        var canViewOnboarding = (await authorizationService.AuthorizeAsync(User, "reporting:view-onboarding")).Succeeded;

        // Bug fix: this was previously hardcoded to true for every caller who passed the baseline
        // reporting:view policy, which meant a Recruiter with no HR/Manager role saw this HR-category
        // report in their Recruitment reports list. Gated on a dedicated Manager/HrAdministrator-only
        // policy instead — see reporting:view-workload-actions in IdentityModule.cs.
        var canViewWorkloadActions = (await authorizationService.AuthorizeAsync(User, "reporting:view-workload-actions")).Succeeded;

        var result = await handler.HandleAsync(request, canViewRecruitment, canViewHr, canViewEmployeeStarter, canViewLeaveSummary, canViewProbation, canViewOnboarding, canViewWorkloadActions, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
