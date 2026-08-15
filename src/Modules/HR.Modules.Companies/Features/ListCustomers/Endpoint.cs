using FastEndpoints;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.ListCustomers;

internal sealed class Endpoint(
    ListCustomersHandler handler) : Endpoint<ListCustomersRequest, ListCustomersResponse>
{
    public override void Configure()
    {
        Get("/api/companies/admin/customers");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(ListCustomersRequest req, CancellationToken cancellationToken)
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
