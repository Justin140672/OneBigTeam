using FastEndpoints;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.ForceCustomerReadOnly;

internal sealed class Endpoint(
    ForceCustomerReadOnlyHandler handler) : Endpoint<ForceCustomerReadOnlyRequest, ForceCustomerReadOnlyResponse>
{
    public override void Configure()
    {
        Post("/api/companies/admin/customers/{companyId:guid}/subscription/force-read-only");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(ForceCustomerReadOnlyRequest req, CancellationToken cancellationToken)
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

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
