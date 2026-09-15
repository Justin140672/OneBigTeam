using FastEndpoints;
using HR.SharedKernel;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.GetCustomerBillingHistory;

internal sealed class Endpoint(
    GetCustomerBillingHistoryHandler handler)
    : Endpoint<GetCustomerBillingHistoryRequest, GetCustomerBillingHistoryResponse>
{
    public override void Configure()
    {
        Get("/api/companies/admin/customers/{companyId:guid}/billing-history");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(GetCustomerBillingHistoryRequest req, CancellationToken cancellationToken)
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
