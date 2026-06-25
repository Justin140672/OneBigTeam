using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.ListEmployees;

internal sealed class Endpoint(
    ListEmployeesHandler handler) : Endpoint<ListEmployeesRequest, ListEmployeesResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        ListEmployeesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
