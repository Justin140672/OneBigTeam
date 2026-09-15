using FastEndpoints;
using HR.SharedKernel;

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
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
