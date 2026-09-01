using FastEndpoints;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.ExecuteCustomerDeletion;

internal sealed class Endpoint(
    ExecuteCustomerDeletionHandler handler) : Endpoint<ExecuteCustomerDeletionRequest, ExecuteCustomerDeletionResponse>
{
    public override void Configure()
    {
        Post("/api/companies/admin/customers/{companyId:guid}/subscription/execute-deletion");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(ExecuteCustomerDeletionRequest req, CancellationToken cancellationToken)
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

            // NFR-07: a legal hold blocking deletion is a conflict with current resource state,
            // not a malformed request — surface it as 409 so callers can distinguish it.
            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
