using FastEndpoints;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.GetFailedPayments;

internal sealed class Endpoint(
    GetFailedPaymentsHandler handler) : Endpoint<GetFailedPaymentsRequest, GetFailedPaymentsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/admin/failed-payments");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(GetFailedPaymentsRequest req, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(req, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "unauthorized")
            {
                await Send.ResultAsync(TypedResults.Unauthorized());
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
