using FastEndpoints;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.GetSystemHealth;

internal sealed class Endpoint(
    GetSystemHealthHandler handler) : EndpointWithoutRequest<GetSystemHealthResponse>
{
    public override void Configure()
    {
        Get("/api/companies/admin/system-health");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetSystemHealthRequest(), cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error.Code == "unauthorized")
            {
                await Send.ResultAsync(TypedResults.Unauthorized());
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
