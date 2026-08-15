using FastEndpoints;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.GetCustomerSupportView;

internal sealed class Endpoint(
    GetCustomerSupportViewHandler handler) : Endpoint<GetCustomerSupportViewRequest, GetCustomerSupportViewResponse>
{
    public override void Configure()
    {
        Get("/api/companies/admin/customers/{companyId:guid}/support-view");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(GetCustomerSupportViewRequest req, CancellationToken cancellationToken)
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
