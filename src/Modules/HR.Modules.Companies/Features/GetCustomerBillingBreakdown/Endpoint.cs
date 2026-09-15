using FastEndpoints;
using HR.SharedKernel;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.GetCustomerBillingBreakdown;

internal sealed class Endpoint(
    GetCustomerBillingBreakdownHandler handler)
    : Endpoint<GetCustomerBillingBreakdownRequest, GetCustomerBillingBreakdownResponse>
{
    public override void Configure()
    {
        Get("/api/companies/admin/customers/{companyId:guid}/billing-breakdown");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(GetCustomerBillingBreakdownRequest req, CancellationToken cancellationToken)
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
