using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace HR.Modules.Reporting.ReportRegistry;

internal static class ReportAccessGateEvaluator
{
    public static async Task<ReportAccessGates> EvaluateAsync(
        IAuthorizationService authorizationService,
        ClaimsPrincipal user)
    {
        var canViewRecruitment = (await authorizationService.AuthorizeAsync(user, "reporting:view-recruitment")).Succeeded;
        var canViewHr = (await authorizationService.AuthorizeAsync(user, "reporting:view-hr")).Succeeded;
        var canViewEmployeeStarter = (await authorizationService.AuthorizeAsync(user, "reporting:view-employee-starter")).Succeeded;
        var canViewLeaveSummary = (await authorizationService.AuthorizeAsync(user, "reporting:view-leave-summary")).Succeeded;
        var canViewProbation = (await authorizationService.AuthorizeAsync(user, "reporting:view-probation")).Succeeded;
        var canViewOnboarding = (await authorizationService.AuthorizeAsync(user, "reporting:view-onboarding")).Succeeded;
        var canViewWorkloadActions = (await authorizationService.AuthorizeAsync(user, "reporting:view-workload-actions")).Succeeded;
        var canViewEqualityDiversity = (await authorizationService.AuthorizeAsync(user, "reporting:view-equality")).Succeeded;

        return new ReportAccessGates(
            canViewRecruitment,
            canViewHr,
            canViewEmployeeStarter,
            canViewLeaveSummary,
            canViewProbation,
            canViewOnboarding,
            canViewWorkloadActions,
            canViewEqualityDiversity);
    }
}
