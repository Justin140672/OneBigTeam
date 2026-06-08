using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.DeactivateDepartment;

internal sealed class Endpoint(
    DeactivateDepartmentHandler handler) : Endpoint<DeactivateDepartmentRequest>
{
    public override void Configure()
    {
        Delete("/api/companies/{companyId:guid}/departments/{id:guid}");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        DeactivateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error.Code == "not_found")
            {
                await SendResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
                return;
            }

            await SendResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await SendNoContentAsync(cancellationToken);
    }
}
