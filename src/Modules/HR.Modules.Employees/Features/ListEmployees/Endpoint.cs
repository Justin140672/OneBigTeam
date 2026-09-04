using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.ListEmployees;

internal sealed class Endpoint(
    ListEmployeesHandler handler) : Endpoint<ListEmployeesRequest, ListEmployeesResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees");
        // ADM-05: the employee administration list (the full EmployeeList grid with account status,
        // audit columns, etc.) is HR Administrator only — "employee:manage". A plain Employee, a
        // Manager, a Recruiter, or a Company-Administrator-only user must not enumerate the whole
        // workforce here. Screens that only need a name/id picker (e.g. the vacancy hiring-manager
        // dropdown) use the lighter "employee:read"-gated /employees/selectable endpoint instead
        // (see ListSelectableEmployeesEndpoint).
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        ListEmployeesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
