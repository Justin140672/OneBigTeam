using FastEndpoints;
using HR.SharedKernel;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.GetCustomerDetails;

internal sealed class Endpoint(
    GetCustomerDetailsHandler handler) : Endpoint<GetCustomerDetailsRequest, GetCustomerDetailsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/admin/customers/{companyId:guid}");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(GetCustomerDetailsRequest req, CancellationToken cancellationToken)
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
