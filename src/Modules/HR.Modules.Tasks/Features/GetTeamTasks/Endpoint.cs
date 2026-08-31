using FastEndpoints;
using HR.Modules.Tasks.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Tasks.Features.GetTeamTasks;

internal sealed class Endpoint(
    GetTeamTasksHandler handler,
    ICurrentUser currentUser,
    TasksResourceAuthorizer resourceAuthorizer) : Endpoint<GetTeamTasksRequest, GetTeamTasksResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{managerId:guid}/team-tasks");
        Policies("role:employee");
    }

    public override async Task HandleAsync(GetTeamTasksRequest request, CancellationToken cancellationToken)
    {
        // DSH-01: derive the requesting identity from the authenticated principal. The {managerId}
        // route value is a browser-supplied parameter and must never be treated as the
        // authorization identity — "role:employee" alone only proves tenant membership, so without
        // this check any employee could read any manager's team tasks by editing the URL.
        // NOT User.FindFirst("sub") — that's the raw Supabase Auth user id, not this app's
        // resolved Employee/UserId (see GetMyEmployee/Endpoint.cs for the rationale).
        if (currentUser.UserId is not { } callerEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        // Tenant isolation is already enforced by TenantRouteAuthorizationMiddleware; re-assert it
        // defensively here (mirrors GetEmployeeTasks/Endpoint.cs). Reads the DB-resolved tenant via
        // ICurrentUser, not a raw "company_id" JWT claim — real Supabase tokens never carry one.
        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        // DSH-01: team-task retrieval is permitted only for
        //   - the requested manager themselves (their own team),
        //   - a manager higher in the requested manager's reporting hierarchy (the requested
        //     manager's team is a sub-tree of the caller's authorized hierarchy), or
        //   - a caller with company-wide authority (HR Administrator).
        // CanAccessEmployeeTasksAsync evaluates exactly this (self / hierarchy / company-wide) with
        // the requested manager as the target. Delegated company-wide authority beyond HR
        // Administrator is a DSH-02 concern (authorized-hierarchy / delegation service) — see notes.
        if (!await resourceAuthorizer.CanAccessEmployeeTasksAsync(
                request.CompanyId, callerEmployeeId, request.ManagerId, cancellationToken))
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var response = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
