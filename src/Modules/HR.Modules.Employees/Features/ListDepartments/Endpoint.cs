using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.ListDepartments;

internal sealed class Endpoint(
    ListDepartmentsHandler handler) : Endpoint<ListDepartmentsRequest, ListDepartmentsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/departments");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        ListDepartmentsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        await SendAsync(result.Value!, StatusCodes.Status200OK, cancellationToken);
    }
}
