using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetLocation;

internal sealed class Endpoint(
    GetLocationHandler handler) : Endpoint<GetLocationRequest, GetLocationResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/locations/{id:guid}");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        GetLocationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
