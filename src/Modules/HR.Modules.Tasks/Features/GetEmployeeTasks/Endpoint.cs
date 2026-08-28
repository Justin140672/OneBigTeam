using FastEndpoints;
using HR.Modules.Tasks.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Tasks.Features.GetEmployeeTasks;

internal sealed class Endpoint(
    GetEmployeeTasksHandler handler,
    ICurrentUser currentUser,
    TasksResourceAuthorizer resourceAuthorizer) : Endpoint<GetEmployeeTasksRequest, GetEmployeeTasksResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/tasks");
        Policies("role:employee");
    }

    public override async Task HandleAsync(GetEmployeeTasksRequest request, CancellationToken cancellationToken)
    {
        // NOT User.FindFirst("sub") — that's the raw Supabase Auth user id, not this app's
        // resolved Employee/UserId (see GetMyEmployee/Endpoint.cs for the rationale).
        if (currentUser.UserId is not { } callerEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        // Reads the DB-resolved tenant via ICurrentUser, not a raw "company_id" JWT claim — real
        // Supabase-issued tokens never carry one (see TenantRouteAuthorizationMiddleware).
        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        // IAM-07: self, any manager in the target employee's complete reporting hierarchy, or an
        // HR Administrator may list this employee's tasks. "role:employee" above only proves the
        // caller holds a role — it never proves a relationship to this specific employeeId.
        if (!await resourceAuthorizer.CanAccessEmployeeTasksAsync(
                request.CompanyId, callerEmployeeId, request.EmployeeId, cancellationToken))
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var response = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
