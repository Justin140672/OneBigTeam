using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.ListEmployees;

internal sealed class Endpoint(
    ListEmployeesHandler handler) : Endpoint<ListEmployeesRequest, ListEmployeesResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees");
        // ADM-05: employee administration list — Manager / Recruiter / HR Administrator only.
        // A plain Employee or a Company-Administrator-only user must not enumerate the workforce.
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
