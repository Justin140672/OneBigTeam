using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.DeactivateLocation;

internal sealed class Endpoint(
    DeactivateLocationHandler handler) : Endpoint<DeactivateLocationRequest>
{
    public override void Configure()
    {
        Delete("/api/companies/{companyId:guid}/locations/{id:guid}");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        DeactivateLocationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.NoContentAsync(cancellationToken);
    }
}
