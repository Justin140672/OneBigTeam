using FastEndpoints;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Authorization;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetManagerTeamStatusSummary;

/// <summary>
/// DSH-05: GET the authoritative team-status summary for a manager's reporting sub-tree.
///
/// The <c>employee:read</c> policy only proves the caller holds an administrative-read role
/// (Manager / Recruiter / HR Administrator). The browser-supplied <c>{managerId}</c> route value
/// is then authorized against the authenticated caller: they may view that manager's team only if
/// they ARE that manager, sit ABOVE them in the reporting tree, or hold company-wide employee
/// access. See specifications/architecture/11-manager-hierarchy-scope.md and GetTeamSicknessToday.
/// </summary>
internal sealed class Endpoint(
    GetManagerTeamStatusSummaryHandler handler,
    ICurrentUser currentUser,
    IAuthorizationService authorizationService,
    IDirectReportsReader directReportsReader)
    : Endpoint<GetManagerTeamStatusSummaryRequest, GetManagerTeamStatusSummaryResponse>
{
    // Mirrors HR.Modules.Identity.Domain.SystemPermissions.EmployeeEdit. Employees cannot
    // reference Identity's internal SystemPermissions directly; the id is duplicated here as the
    // sanctioned escape hatch (same approach as SicknessResourceAuthorizer). EmployeeEdit is held
    // ONLY by HR Administrator — the deliberate "company-wide people access" proxy here. NOT
    // EmployeeRead: that is also held by every Manager, which would let any manager view any other
    // manager's team-status summary instead of just their own reporting sub-tree (DSH-02).
    private static readonly Guid CompanyWidePeopleAccessPermissionId = new("00000000-0000-0000-0001-000000000004");

    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{managerId:guid}/team-status-summary");
        Policies("employee:read");
    }

    public override async Task HandleAsync(
        GetManagerTeamStatusSummaryRequest request,
        CancellationToken cancellationToken)
    {
        // NOT User.FindFirst("sub") — that is the raw Supabase Auth user id, not this app's
        // resolved Employee/UserId. See GetMyEmployee/Endpoint.cs.
        if (currentUser.UserId is not { } callerEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var authorizer = new EmployeeResourceAuthorizer(
            (id, ct) => authorizationService.HasPermissionAsync(id, CompanyWidePeopleAccessPermissionId, ct),
            directReportsReader.GetAllDescendantIdsAsync);

        var allowed = await authorizer.CanAccessAsync(
            request.CompanyId, request.CompanyId, callerEmployeeId, request.ManagerId, cancellationToken);

        if (!allowed)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var response = await handler.HandleAsync(request.CompanyId, request.ManagerId, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
